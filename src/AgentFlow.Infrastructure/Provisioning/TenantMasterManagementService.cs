using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Provisioning;
using AgentFlow.Infrastructure.Documents;
using AgentFlow.Infrastructure.Persistence;
using AgentFlow.Infrastructure.Storage;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.Provisioning;

/// <summary>
/// Gestión de maestros para partners externos — capa GENÉRICA por TenantId.
/// El controller de cada partner (hoy Ludo) resuelve su identidad/HMAC y delega aquí.
///
/// Reglas duras:
/// <list type="bullet">
///   <item>Todo acotado al <c>tenantId</c> recibido — imposible tocar otro tenant.</item>
///   <item>Vertical "seguro": el gate de autenticación vetado se ANTEPONE SIEMPRE al
///     prompt (regenerado o literal). El partner no puede quitarlo.</item>
///   <item>Objetivo → regeneración con versionado seguro: maestro ACTIVO → borrador
///     nuevo (no se pisa el prompt vivo); BORRADOR → en sitio.</item>
///   <item>Activar reusa la semántica del portal: promueve a primario solo si el
///     agente no tiene otro primario activo.</item>
/// </list>
/// </summary>
public sealed class TenantMasterManagementService(
    AgentFlowDbContext db,
    ICampaignTemplateGenerator generator,
    IBlobStorageService blobStorage,
    IBackgroundJobClient backgroundJobs,
    ILogger<TenantMasterManagementService> log) : ITenantMasterManagementService
{
    private const int MaxDocumentsPerTemplate = 20;
    private const long MaxBytesPerDocument = 20L * 1024 * 1024; // 20 MB por API (base64)

    public async Task<IReadOnlyList<MasterState>> GetMastersAsync(Guid tenantId, CancellationToken ct)
    {
        var rows = await (
            from t in db.CampaignTemplates
            join a in db.AgentDefinitions on t.AgentDefinitionId equals a.Id
            where t.TenantId == tenantId
            select new
            {
                AgentSlug = a.Name,
                t.Id, t.Name, t.IsActive, t.IsPrimaryForAgent, t.Objetivo,
                t.SendFrom, t.SendUntil, t.UpdatedAt,
                DocumentCount = db.CampaignTemplateDocuments.Count(d => d.CampaignTemplateId == t.Id),
            }).ToListAsync(ct);

        return rows.Select(r => new MasterState(
            r.AgentSlug, r.Id, r.Name, r.IsActive, r.IsPrimaryForAgent,
            r.Objetivo, r.SendFrom, r.SendUntil, r.DocumentCount, r.UpdatedAt)).ToList();
    }

    public async Task<IReadOnlyList<DocumentState>?> ListDocumentsAsync(Guid tenantId, string agentSlug, CancellationToken ct)
    {
        var master = await ResolveMasterAsync(tenantId, agentSlug, ct);
        if (master is null) return null; // agente/maestro inexistente → el controller responde 400

        return await db.CampaignTemplateDocuments
            .Where(d => d.CampaignTemplateId == master.Id && d.TenantId == tenantId)
            .OrderByDescending(d => d.UploadedAt)
            .Select(d => new DocumentState(d.Id, d.FileName, d.Description, d.FileSizeBytes, d.UploadedAt, d.IndexedAt != null))
            .ToListAsync(ct);
    }

    public async Task<MasterManagementResult> UpdateMasterAsync(Guid tenantId, UpdateMasterRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.AgentSlug))
            return new(false, "agentSlug es requerido.");

        CampaignTemplate? master;
        if (req.TemplateId is { } templateId)
        {
            // Versión específica (ej. activar el borrador de una regeneración). Acotado
            // al tenant Y al agente indicado — un id ajeno no resuelve.
            var agentId = await db.AgentDefinitions
                .Where(a => a.TenantId == tenantId && a.Name == req.AgentSlug)
                .Select(a => (Guid?)a.Id).FirstOrDefaultAsync(ct);
            master = agentId is null ? null : await db.CampaignTemplates.FirstOrDefaultAsync(t =>
                t.Id == templateId && t.TenantId == tenantId && t.AgentDefinitionId == agentId.Value, ct);
            if (master is null)
                return new(false, $"templateId '{templateId}' no existe para el agente '{req.AgentSlug}' de este tenant.");
        }
        else
        {
            master = await ResolveMasterAsync(tenantId, req.AgentSlug, ct);
            if (master is null)
                return new(false, $"No existe un maestro para el agente '{req.AgentSlug}' en este tenant.");
        }

        // Validar horarios ANTES de tocar nada (todo-o-nada).
        TimeOnly? from = null, until = null;
        if (req.SendFrom is not null)
        {
            if (!TimeOnly.TryParse(req.SendFrom, out var f))
                return new(false, $"sendFrom inválido: '{req.SendFrom}' (formato HH:mm).");
            from = f;
        }
        if (req.SendUntil is not null)
        {
            if (!TimeOnly.TryParse(req.SendUntil, out var u))
                return new(false, $"sendUntil inválido: '{req.SendUntil}' (formato HH:mm).");
            until = u;
        }

        var tipoNegocio = await GetTipoNegocioAsync(tenantId, ct);
        var messages = new List<string>();
        var target = master; // puede cambiar a un borrador nuevo si se regenera un activo

        // ── Prompt ───────────────────────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(req.Objetivo))
        {
            // Regenerar con IA (etapas vigentes del tenant como contexto de criterios).
            var etapas = await db.StageLabelMaps
                .Where(s => s.TenantId == tenantId && s.IsActive)
                .OrderBy(s => s.Orden)
                .Select(s => new StageInfo(s.Nombre, s.Orden))
                .ToListAsync(ct);
            var tenantKey = await db.Tenants.Where(t => t.Id == tenantId)
                .Select(t => t.LlmApiKey).FirstOrDefaultAsync(ct);

            var gen = await generator.GenerateAsync(
                new GenerateTemplateRequest(tipoNegocio, req.AgentSlug, req.Objetivo, etapas),
                tenantApiKey: tenantKey, ct);

            if (master.IsActive)
            {
                // Versionado seguro: NO pisar el prompt vivo — borrador nuevo.
                target = new CampaignTemplate
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    AgentDefinitionId = master.AgentDefinitionId,
                    Name = $"{master.Name} (v{DateTime.UtcNow:yyyyMMdd-HHmm})",
                    Objetivo = req.Objetivo,
                    GeneratedByLlm = gen.UsedLlm,
                    SystemPrompt = gen.SystemPrompt,
                    IsActive = false,
                    IsPrimaryForAgent = false,
                    ActiveFlowId = master.ActiveFlowId,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                };
                db.CampaignTemplates.Add(target);
                messages.Add("Prompt regenerado en un BORRADOR nuevo (el maestro activo no se tocó); revisarlo y activarlo.");
            }
            else
            {
                master.Objetivo = req.Objetivo;
                master.SystemPrompt = gen.SystemPrompt;
                master.GeneratedByLlm = gen.UsedLlm;
                messages.Add("Prompt regenerado en sitio (el maestro estaba en borrador).");
            }
        }
        else if (!string.IsNullOrWhiteSpace(req.SystemPrompt))
        {
            target.SystemPrompt = ApplyVettedGate(tipoNegocio, req.SystemPrompt);
            target.GeneratedByLlm = false;
            messages.Add(IsSeguro(tipoNegocio)
                ? "Prompt actualizado (vertical seguro: el gate de autenticación se antepone siempre)."
                : "Prompt actualizado.");
        }

        // ── Horarios de envío ────────────────────────────────────────────────────
        if (from is not null) { target.SendFrom = from.Value.ToString("HH:mm"); messages.Add($"sendFrom={target.SendFrom}."); }
        if (until is not null) { target.SendUntil = until.Value.ToString("HH:mm"); messages.Add($"sendUntil={target.SendUntil}."); }

        // ── Activación / desactivación (sobre el maestro ORIGINAL resuelto) ─────
        if (req.Activar == true)
        {
            var currentPrimary = await db.CampaignTemplates.FirstOrDefaultAsync(t =>
                t.TenantId == tenantId
                && t.AgentDefinitionId == target.AgentDefinitionId
                && t.Id != target.Id && t.IsActive && t.IsPrimaryForAgent, ct);

            if (currentPrimary is not null && req.TemplateId is not null)
            {
                // templateId explícito = "esta es la versión nueva" → SWAP: la versión
                // indicada reemplaza a la primaria (que baja a secundaria activa,
                // recuperable). Sin templateId se mantiene la regla conservadora.
                currentPrimary.IsPrimaryForAgent = false;
                currentPrimary.UpdatedAt = DateTime.UtcNow;
                target.IsActive = true;
                target.IsPrimaryForAgent = true;
                messages.Add($"Maestro activado como PRIMARIO (reemplaza a la versión anterior '{currentPrimary.Name}', que queda como secundaria).");
            }
            else
            {
                target.IsActive = true;
                target.IsPrimaryForAgent = currentPrimary is null;
                messages.Add(target.IsPrimaryForAgent
                    ? "Maestro activado como PRIMARIO del agente."
                    : "Maestro activado como secundario (el agente ya tiene otro primario).");
            }
        }
        else if (req.Activar == false)
        {
            target.IsActive = false;
            target.IsPrimaryForAgent = false;
            messages.Add("Maestro desactivado (las campañas ya lanzadas siguen con su config).");
        }

        if (messages.Count == 0)
            return new(false, "Nada que actualizar: enviá objetivo, systemPrompt, sendFrom/sendUntil o activar.");

        target.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        log.LogInformation("[TenantMgmt] Maestro {TemplateId} (tenant {TenantId}) actualizado: {Msg}",
            target.Id, tenantId, string.Join(" ", messages));

        return new(true, string.Join(" ", messages), target.Id, target.IsActive, target.IsPrimaryForAgent);
    }

    public async Task<MasterManagementResult> AddDocumentAsync(Guid tenantId, UploadDocumentRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.FileName) || string.IsNullOrWhiteSpace(req.Base64))
            return new(false, "fileName y base64 son requeridos.");

        var master = await ResolveMasterAsync(tenantId, req.AgentSlug, ct);
        if (master is null)
            return new(false, $"No existe un maestro para el agente '{req.AgentSlug}' en este tenant.");

        byte[] bytes;
        try { bytes = Convert.FromBase64String(req.Base64); }
        catch (FormatException) { return new(false, "base64 inválido."); }

        if (bytes.Length > MaxBytesPerDocument)
            return new(false, $"El archivo excede el límite de {MaxBytesPerDocument / (1024 * 1024)} MB.");

        // Solo PDF (magic bytes %PDF) — el pipeline RAG indexa PDFs.
        if (bytes.Length < 4 || bytes[0] != 0x25 || bytes[1] != 0x50 || bytes[2] != 0x44 || bytes[3] != 0x46)
            return new(false, "Solo se aceptan archivos PDF.");

        var count = await db.CampaignTemplateDocuments
            .CountAsync(d => d.CampaignTemplateId == master.Id && d.TenantId == tenantId, ct);
        if (count >= MaxDocumentsPerTemplate)
            return new(false, $"El maestro ya tiene el máximo de {MaxDocumentsPerTemplate} documentos.");

        var blobPath = $"{tenantId}/campaign-templates/{master.Id}/{Guid.NewGuid()}_{req.FileName}";
        await using var stream = new MemoryStream(bytes);
        var blobUrl = await blobStorage.UploadAsync(blobPath, stream, "application/pdf", ct);

        var doc = new CampaignTemplateDocument
        {
            Id = Guid.NewGuid(),
            CampaignTemplateId = master.Id,
            TenantId = tenantId,
            FileName = req.FileName,
            BlobUrl = blobUrl,
            ContentType = "application/pdf",
            FileSizeBytes = bytes.Length,
            UploadedAt = DateTime.UtcNow,
            Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
        };
        db.CampaignTemplateDocuments.Add(doc);
        await db.SaveChangesAsync(ct);

        // Indexado RAG asíncrono (no-bloqueante: si Hangfire está caído, el PDF queda
        // subido y el admin puede reindexar desde la UI).
        try { backgroundJobs.Enqueue<DocumentIndexerHangfireJob>(j => j.IndexAsync(doc.Id, false)); }
        catch (Exception ex) { log.LogWarning("[TenantMgmt] No se pudo encolar indexado RAG de {DocId}: {Msg}", doc.Id, ex.Message); }

        return new(true, $"Documento '{req.FileName}' cargado ({bytes.Length / 1024} KB); indexación en curso.",
            master.Id, DocumentId: doc.Id);
    }

    public async Task<MasterManagementResult> RemoveDocumentAsync(Guid tenantId, string agentSlug, Guid documentId, CancellationToken ct)
    {
        var master = await ResolveMasterAsync(tenantId, agentSlug, ct);
        if (master is null)
            return new(false, $"No existe un maestro para el agente '{agentSlug}' en este tenant.");

        var doc = await db.CampaignTemplateDocuments
            .FirstOrDefaultAsync(d => d.Id == documentId && d.CampaignTemplateId == master.Id && d.TenantId == tenantId, ct);
        if (doc is null) return new(false, "Documento no encontrado.");

        try
        {
            var blobPath = ExtractBlobPath(doc.BlobUrl);
            await blobStorage.DeleteAsync(blobPath, ct);
        }
        catch (Exception ex)
        {
            // El blob puede ya no existir; la fila igual se elimina.
            log.LogWarning("[TenantMgmt] No se pudo borrar blob de {DocId}: {Msg}", documentId, ex.Message);
        }

        db.CampaignTemplateDocuments.Remove(doc);
        await db.SaveChangesAsync(ct);
        return new(true, $"Documento '{doc.FileName}' eliminado.", master.Id, DocumentId: documentId);
    }

    public async Task<MasterManagementResult> UpdateTenantHoursAsync(Guid tenantId, UpdateTenantHoursRequest req, CancellationToken ct)
    {
        if (!TimeOnly.TryParse(req.BusinessHoursStart, out var start))
            return new(false, $"businessHoursStart inválido: '{req.BusinessHoursStart}' (formato HH:mm).");
        if (!TimeOnly.TryParse(req.BusinessHoursEnd, out var end))
            return new(false, $"businessHoursEnd inválido: '{req.BusinessHoursEnd}' (formato HH:mm).");

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return new(false, "Tenant no encontrado.");

        tenant.BusinessHoursStart = start;
        tenant.BusinessHoursEnd = end;
        await db.SaveChangesAsync(ct);
        return new(true, $"Horario de atención actualizado: {start:HH\\:mm}–{end:HH\\:mm} ({tenant.TimeZone}).");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    /// <summary>Maestro del agente: primario activo si existe; si no, el más reciente.</summary>
    private async Task<CampaignTemplate?> ResolveMasterAsync(Guid tenantId, string agentSlug, CancellationToken ct)
    {
        var agentId = await db.AgentDefinitions
            .Where(a => a.TenantId == tenantId && a.Name == agentSlug)
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync(ct);
        if (agentId is null) return null;

        return await db.CampaignTemplates
            .Where(t => t.TenantId == tenantId && t.AgentDefinitionId == agentId.Value)
            .OrderByDescending(t => t.IsActive && t.IsPrimaryForAgent)
            .ThenByDescending(t => t.UpdatedAt)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<string> GetTipoNegocioAsync(Guid tenantId, CancellationToken ct) =>
        await db.LudoTenantMaps.Where(m => m.TenantId == tenantId)
            .Select(m => m.TipoNegocio).FirstOrDefaultAsync(ct) ?? "seguro"; // sin mapa → asumir el vertical más estricto

    private static bool IsSeguro(string tipoNegocio) =>
        string.Equals(tipoNegocio, "seguro", StringComparison.OrdinalIgnoreCase);

    private static string ApplyVettedGate(string tipoNegocio, string prompt)
    {
        if (!IsSeguro(tipoNegocio)) return prompt;
        var gate = CampaignTemplateGenerator.VettedSeguroAuthGate;
        return prompt.Contains(gate, StringComparison.Ordinal) ? prompt : gate + "\n\n" + prompt;
    }

    private static string ExtractBlobPath(string blobUrl)
    {
        const string marker = ".blob.core.windows.net/";
        var markerIdx = blobUrl.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIdx < 0) return blobUrl;
        var afterHost = blobUrl[(markerIdx + marker.Length)..];
        var slashIdx = afterHost.IndexOf('/');
        if (slashIdx < 0) return afterHost;
        return Uri.UnescapeDataString(afterHost[(slashIdx + 1)..]);
    }
}
