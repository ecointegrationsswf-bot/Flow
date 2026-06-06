using System.Text.Json;
using System.Text.RegularExpressions;
using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Channels.MetaCloudApi;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.API.Controllers;

// Gestión de plantillas HSM de Meta. TODO se opera con las credenciales (WABA + token)
// de la línea Meta del tenant — nunca valores globales ni de otro tenant.
public record CreateMetaTemplateRequest(
    Guid LineId, string Name, string? Language, string? Category,
    string? HeaderText, string BodyText, string? FooterText,
    List<string>? HeaderSamples, List<string>? BodySamples,
    bool SubmitToMeta = true,
    Guid? CampaignTemplateId = null,
    string? Purpose = null,
    List<string>? BodyMapping = null);   // campo {{n}}→field, paralelo a BodySamples

public record UpdateMetaTemplateRequest(
    string Name, string? Language, string? Category,
    string? HeaderText, string BodyText, string? FooterText,
    List<string>? HeaderSamples, List<string>? BodySamples,
    bool SubmitToMeta = true,
    string? Purpose = null,
    List<string>? BodyMapping = null);

[ApiController]
[Route("api/meta-templates")]
[Authorize]
public partial class MetaTemplatesController(
    ITenantContext tenantCtx,
    AgentFlowDbContext db,
    IMetaTemplateService metaTemplates,
    AgentFlow.Infrastructure.AI.IMetaTemplateGenerator templateGenerator) : ControllerBase
{
    // ── LISTAR (por línea) ───────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid lineId, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;
        var items = await db.MetaMessageTemplates
            .Where(t => t.TenantId == tenantId && t.WhatsAppLineId == lineId)
            .OrderByDescending(t => t.CreatedAt).ThenBy(t => t.SequenceOrder)
            .ToListAsync(ct);
        return Ok(items.Select(Project));
    }

    // ── CAMPOS DISPONIBLES para el mapeo {{n}}→campo (form manual) ───────
    [HttpGet("available-fields")]
    public async Task<IActionResult> AvailableFields([FromQuery] Guid campaignTemplateId, CancellationToken ct)
    {
        var fields = await templateGenerator.GetAvailableFieldsAsync(tenantCtx.TenantId, campaignTemplateId, ct);
        return Ok(new { fields });
    }

    // ── CREAR (+ enviar a Meta, salvo SubmitToMeta=false → DRAFT local) ──
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateMetaTemplateRequest req, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;

        var (line, lineError) = await ResolveMetaLineAsync(req.LineId, tenantId, ct);
        if (lineError is not null) return lineError;

        var name = NormalizeName(req.Name);
        var language = string.IsNullOrWhiteSpace(req.Language) ? "es" : req.Language!.Trim();
        var category = (req.Category ?? MetaTemplateCategories.Utility).ToUpperInvariant();

        var validation = ValidateTemplate(name, category, req.HeaderText, req.BodyText,
            req.HeaderSamples, req.BodySamples);
        if (validation is not null) return validation;

        // Unicidad (línea, nombre, idioma).
        var dup = await db.MetaMessageTemplates.AnyAsync(
            t => t.WhatsAppLineId == line!.Id && t.Name == name && t.Language == language, ct);
        if (dup)
            return Conflict(new { error = $"Ya existe una plantilla '{name}' ({language}) en esta línea." });

        var entity = new MetaMessageTemplate
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            WhatsAppLineId = line!.Id,
            Name = name,
            Language = language,
            Category = category,
            HeaderType = string.IsNullOrWhiteSpace(req.HeaderText) ? null : "TEXT",
            HeaderText = string.IsNullOrWhiteSpace(req.HeaderText) ? null : req.HeaderText!.Trim(),
            BodyText = req.BodyText.Trim(),
            FooterText = string.IsNullOrWhiteSpace(req.FooterText) ? null : req.FooterText!.Trim(),
            VariableSamplesJson = SerializeSamples(req.HeaderSamples, req.BodySamples),
            ParameterMappingJson = SerializeMapping(req.BodyMapping),
            Purpose = MetaTemplatePurposes.IsValid(req.Purpose) ? req.Purpose! : MetaTemplatePurposes.Launch,
            MetaStatus = MetaTemplateStatuses.Draft,
            IsEnabled = true,
            CampaignTemplateId = req.CampaignTemplateId,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = CurrentUserId(),
        };

        // Enviar a Meta (o dejar como DRAFT si SubmitToMeta=false).
        if (req.SubmitToMeta)
        {
            var result = await metaTemplates.CreateAsync(
                line.MetaWabaId!, line.MetaAccessToken!,
                BuildInput(entity), ct);

            if (!result.Success)
                return BadRequest(new { error = result.Error ?? "Meta rechazó la creación de la plantilla." });

            entity.MetaTemplateId = result.MetaTemplateId;
            entity.MetaStatus = string.IsNullOrWhiteSpace(result.Status) ? MetaTemplateStatuses.Pending : result.Status!.ToUpperInvariant();
            entity.LastSyncedAt = DateTime.UtcNow;
        }

        db.MetaMessageTemplates.Add(entity);
        await db.SaveChangesAsync(ct);
        return Ok(Project(entity));
    }

    // ── GENERAR DESDE EL PROMPT (IA) → borradores ──────────────────────
    // Lee el prompt del maestro y produce N plantillas (una por burbuja '~') en
    // estado DRAFT, con mapeo {{n}}→campo. NO las envía a Meta: el usuario revisa
    // y luego "Enviar a Meta". Blindaje por-tenant: línea y maestro filtrados por tenant.
    // Columns/SampleDataJson: solo se envían cuando el maestro NO tiene proceso de descarga
    // y el usuario subió un Excel de muestra (el front lo parsea con /email/parse-sample).
    public record GenerateFromPromptRequest(
        Guid LineId, Guid CampaignTemplateId, string? BaseName,
        List<string>? Columns = null, string? SampleDataJson = null);

    [HttpPost("generate-from-prompt")]
    public async Task<IActionResult> GenerateFromPrompt([FromBody] GenerateFromPromptRequest req, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;
        var (line, lineError) = await ResolveMetaLineAsync(req.LineId, tenantId, ct);
        if (lineError is not null) return lineError;

        var gen = await templateGenerator.GenerateAsync(
            tenantId, req.CampaignTemplateId, req.Columns, req.SampleDataJson, ct);

        // Sin estructura de datos: el front debe pedir el Excel y reintentar.
        if (gen.NeedsStructure)
            return Ok(new { needsStructure = true, message = gen.Error });

        if (!gen.Success)
            return BadRequest(new { error = gen.Error ?? "No se pudo generar la plantilla." });

        var baseName = NormalizeName(string.IsNullOrWhiteSpace(req.BaseName) ? "plantilla_campana" : req.BaseName!);
        var multiple = gen.Templates.Count > 1;
        // Si hay varias burbujas, comparten un BubbleGroupId para que la Fase 2 las
        // envíe en secuencia (SequenceOrder 1..N), replicando la estructura del prompt.
        Guid? groupId = multiple ? Guid.NewGuid() : null;
        var created = new List<MetaMessageTemplate>();
        var userId = CurrentUserId();
        var createdAt = DateTime.UtcNow; // mismo timestamp para todo el set → se agrupan al listar

        for (var i = 0; i < gen.Templates.Count; i++)
        {
            var g = gen.Templates[i];
            // Nombre único por burbuja. Si colisiona, agregamos sufijo incremental.
            var name = multiple ? $"{baseName}_{i + 1}" : baseName;
            name = await EnsureUniqueNameAsync(line!.Id, name, "es", ct);

            // Ordenar variables por placeholder; samples y mapeo en ese orden.
            var ordered = g.Variables.OrderBy(v => v.Placeholder).ToList();
            var bodySamples = ordered.Select(v => v.Sample).ToList();
            var mappingFields = ordered.Select(v => v.Field).ToList();

            created.Add(new MetaMessageTemplate
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                WhatsAppLineId = line.Id,
                Name = name,
                Language = "es",
                Category = MetaTemplateCategories.Utility,
                HeaderType = null,
                HeaderText = null,
                BodyText = g.Body,
                FooterText = g.FooterText,
                VariableSamplesJson = JsonSerializer.Serialize(new { header = new List<string>(), body = bodySamples }),
                // Mapeo {{n}}→campo para la Fase 2 (sustitución al enviar).
                ParameterMappingJson = JsonSerializer.Serialize(new { body = mappingFields }),
                MetaStatus = MetaTemplateStatuses.Draft,
                IsEnabled = true,
                BubbleGroupId = groupId,
                SequenceOrder = i + 1,
                CampaignTemplateId = req.CampaignTemplateId,
                CreatedAt = createdAt,
                CreatedByUserId = userId,
            });
        }

        db.MetaMessageTemplates.AddRange(created);
        await db.SaveChangesAsync(ct);
        return Ok(new { count = created.Count, templates = created.Select(Project) });
    }

    private async Task<string> EnsureUniqueNameAsync(Guid lineId, string name, string language, CancellationToken ct)
    {
        var candidate = name;
        var n = 1;
        while (await db.MetaMessageTemplates.AnyAsync(
            t => t.WhatsAppLineId == lineId && t.Name == candidate && t.Language == language, ct))
        {
            n++;
            candidate = $"{name}_v{n}";
        }
        return candidate;
    }

    // ── EDITAR (solo DRAFT/REJECTED; reenvía si SubmitToMeta) ───────────
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateMetaTemplateRequest req, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;
        var entity = await db.MetaMessageTemplates
            .FirstOrDefaultAsync(t => t.Id == id && t.TenantId == tenantId, ct);
        if (entity is null) return NotFound();

        if (entity.MetaStatus is MetaTemplateStatuses.Approved or MetaTemplateStatuses.Pending)
            return BadRequest(new { error = "No se puede editar una plantilla aprobada o en revisión. Creá una nueva versión." });

        var (line, lineError) = await ResolveMetaLineAsync(entity.WhatsAppLineId, tenantId, ct);
        if (lineError is not null) return lineError;

        var name = NormalizeName(req.Name);
        var language = string.IsNullOrWhiteSpace(req.Language) ? "es" : req.Language!.Trim();
        var category = (req.Category ?? entity.Category).ToUpperInvariant();

        var validation = ValidateTemplate(name, category, req.HeaderText, req.BodyText,
            req.HeaderSamples, req.BodySamples);
        if (validation is not null) return validation;

        entity.Name = name;
        entity.Language = language;
        entity.Category = category;
        entity.HeaderType = string.IsNullOrWhiteSpace(req.HeaderText) ? null : "TEXT";
        entity.HeaderText = string.IsNullOrWhiteSpace(req.HeaderText) ? null : req.HeaderText!.Trim();
        entity.BodyText = req.BodyText.Trim();
        entity.FooterText = string.IsNullOrWhiteSpace(req.FooterText) ? null : req.FooterText!.Trim();
        entity.VariableSamplesJson = SerializeSamples(req.HeaderSamples, req.BodySamples);
        entity.ParameterMappingJson = SerializeMapping(req.BodyMapping);
        if (MetaTemplatePurposes.IsValid(req.Purpose)) entity.Purpose = req.Purpose!;
        entity.UpdatedAt = DateTime.UtcNow;

        if (req.SubmitToMeta)
        {
            var result = await metaTemplates.CreateAsync(
                line!.MetaWabaId!, line.MetaAccessToken!, BuildInput(entity), ct);
            if (!result.Success)
                return BadRequest(new { error = result.Error ?? "Meta rechazó la plantilla." });

            entity.MetaTemplateId = result.MetaTemplateId;
            entity.MetaStatus = string.IsNullOrWhiteSpace(result.Status) ? MetaTemplateStatuses.Pending : result.Status!.ToUpperInvariant();
            entity.MetaRejectedReason = null;
            entity.LastSyncedAt = DateTime.UtcNow;
        }
        else
        {
            entity.MetaStatus = MetaTemplateStatuses.Draft;
        }

        await db.SaveChangesAsync(ct);
        return Ok(Project(entity));
    }

    // ── MAPEO de variables {{n}}→campo (metadata local, permitido en APROBADAS) ──
    // El mapeo NO cambia el contenido aprobado por Meta; es nuestro para sustituir al
    // enviar. Por eso se puede ajustar en cualquier estado (incluida APPROVED).
    public record UpdateMappingRequest(List<string> BodyMapping, string? Purpose = null);

    [HttpPut("{id:guid}/mapping")]
    public async Task<IActionResult> UpdateMapping(Guid id, [FromBody] UpdateMappingRequest req, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;
        var entity = await db.MetaMessageTemplates
            .FirstOrDefaultAsync(t => t.Id == id && t.TenantId == tenantId, ct);
        if (entity is null) return NotFound();

        var bodyVars = CountPlaceholders(entity.BodyText);
        var mapping = (req.BodyMapping ?? new()).Select(s => (s ?? "").Trim()).ToList();
        var filledCount = mapping.Take(bodyVars).Count(s => s.Length > 0);
        if (filledCount < bodyVars)
            return BadRequest(new { error = $"El cuerpo tiene {bodyVars} variable(s); asigná un campo a cada una ({filledCount}/{bodyVars})." });

        entity.ParameterMappingJson = SerializeMapping(mapping.Take(bodyVars).ToList());
        if (MetaTemplatePurposes.IsValid(req.Purpose)) entity.Purpose = req.Purpose!;
        entity.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(Project(entity));
    }

    // ── HABILITAR / DESHABILITAR (flag nuestro, separado de Meta) ───────
    [HttpPost("{id:guid}/enable")]
    public Task<IActionResult> Enable(Guid id, CancellationToken ct) => SetEnabled(id, true, ct);

    [HttpPost("{id:guid}/disable")]
    public Task<IActionResult> Disable(Guid id, CancellationToken ct) => SetEnabled(id, false, ct);

    private async Task<IActionResult> SetEnabled(Guid id, bool enabled, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;
        var entity = await db.MetaMessageTemplates
            .FirstOrDefaultAsync(t => t.Id == id && t.TenantId == tenantId, ct);
        if (entity is null) return NotFound();
        entity.IsEnabled = enabled;
        entity.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(Project(entity));
    }

    // ── ENVIAR A META un borrador existente (botón directo en la lista) ─
    // Toma una plantilla en DRAFT (o REJECTED) y la manda a revisión de Meta sin
    // tener que abrir el formulario. Blindaje por-tenant: WABA+token de la línea.
    [HttpPost("{id:guid}/submit")]
    public async Task<IActionResult> Submit(Guid id, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;
        var entity = await db.MetaMessageTemplates
            .FirstOrDefaultAsync(t => t.Id == id && t.TenantId == tenantId, ct);
        if (entity is null) return NotFound();

        if (entity.MetaStatus is MetaTemplateStatuses.Approved or MetaTemplateStatuses.Pending)
            return BadRequest(new { error = "La plantilla ya está en revisión o aprobada." });

        var (line, lineError) = await ResolveMetaLineAsync(entity.WhatsAppLineId, tenantId, ct);
        if (lineError is not null) return lineError;

        // Revalidar variables vs ejemplos antes de enviar (Meta rechaza si no cuadran).
        var (header, body) = DeserializeSamples(entity.VariableSamplesJson);
        var validation = ValidateTemplate(entity.Name, entity.Category, entity.HeaderText, entity.BodyText, header, body);
        if (validation is not null) return validation;

        var result = await metaTemplates.CreateAsync(line!.MetaWabaId!, line.MetaAccessToken!, BuildInput(entity), ct);
        if (!result.Success)
            return BadRequest(new { error = result.Error ?? "Meta rechazó la plantilla." });

        entity.MetaTemplateId = result.MetaTemplateId;
        entity.MetaStatus = string.IsNullOrWhiteSpace(result.Status) ? MetaTemplateStatuses.Pending : result.Status!.ToUpperInvariant();
        entity.MetaRejectedReason = null;
        entity.LastSyncedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(Project(entity));
    }

    // ── SINCRONIZAR estado con Meta (botón manual; respaldo del webhook) ─
    [HttpPost("{id:guid}/sync")]
    public async Task<IActionResult> Sync(Guid id, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;
        var entity = await db.MetaMessageTemplates
            .FirstOrDefaultAsync(t => t.Id == id && t.TenantId == tenantId, ct);
        if (entity is null) return NotFound();

        var (line, lineError) = await ResolveMetaLineAsync(entity.WhatsAppLineId, tenantId, ct);
        if (lineError is not null) return lineError;

        var remote = await metaTemplates.ListAsync(line!.MetaWabaId!, line.MetaAccessToken!, ct);
        var match = remote.FirstOrDefault(r =>
            (entity.MetaTemplateId is not null && r.Id == entity.MetaTemplateId)
            || (r.Name == entity.Name && r.Language == entity.Language));

        if (match is null)
            return Ok(Project(entity)); // no encontrada en Meta (aún no propagada o DRAFT)

        entity.MetaStatus = string.IsNullOrWhiteSpace(match.Status) ? entity.MetaStatus : match.Status.ToUpperInvariant();
        entity.MetaRejectedReason = match.RejectedReason;
        if (entity.MetaTemplateId is null) entity.MetaTemplateId = match.Id;
        if (!string.IsNullOrWhiteSpace(match.Category)) entity.Category = match.Category.ToUpperInvariant();
        entity.LastSyncedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(Project(entity));
    }

    // ── SINCRONIZAR TODO: importar/actualizar las plantillas del WABA ───
    // Trae TODAS las plantillas que viven en Meta (creadas en Business Manager o en
    // sesiones anteriores) y las inserta/actualiza en nuestra BD. Resuelve por
    // MetaTemplateId o (nombre+idioma). Blindaje por-tenant: usa WABA+token de la línea.
    [HttpPost("sync-all")]
    public async Task<IActionResult> SyncAll([FromQuery] Guid lineId, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;
        var (line, lineError) = await ResolveMetaLineAsync(lineId, tenantId, ct);
        if (lineError is not null) return lineError;

        var remote = await metaTemplates.ListAsync(line!.MetaWabaId!, line.MetaAccessToken!, ct);

        var existing = await db.MetaMessageTemplates
            .Where(t => t.TenantId == tenantId && t.WhatsAppLineId == line.Id)
            .ToListAsync(ct);

        var userId = CurrentUserId();
        int imported = 0, updated = 0;
        foreach (var r in remote)
        {
            var match = existing.FirstOrDefault(e =>
                (!string.IsNullOrEmpty(r.Id) && e.MetaTemplateId == r.Id)
                || (e.Name == r.Name && e.Language == r.Language));

            if (match is null)
            {
                db.MetaMessageTemplates.Add(new MetaMessageTemplate
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    WhatsAppLineId = line.Id,
                    Name = r.Name,
                    Language = string.IsNullOrWhiteSpace(r.Language) ? "es" : r.Language,
                    Category = string.IsNullOrWhiteSpace(r.Category) ? MetaTemplateCategories.Utility : r.Category.ToUpperInvariant(),
                    HeaderType = string.IsNullOrWhiteSpace(r.HeaderText) ? null : "TEXT",
                    HeaderText = r.HeaderText,
                    BodyText = r.BodyText ?? "",
                    FooterText = r.FooterText,
                    MetaTemplateId = r.Id,
                    MetaStatus = NormalizeStatus(r.Status),
                    MetaRejectedReason = r.RejectedReason,
                    IsEnabled = true,
                    CreatedAt = DateTime.UtcNow,
                    LastSyncedAt = DateTime.UtcNow,
                    CreatedByUserId = userId,
                });
                imported++;
            }
            else
            {
                // Meta es la fuente de verdad para plantillas ya enviadas: actualizamos
                // estado y contenido. No tocamos IsEnabled (flag local del usuario).
                match.MetaStatus = NormalizeStatus(r.Status);
                match.MetaRejectedReason = r.RejectedReason;
                if (!string.IsNullOrEmpty(r.Id) && string.IsNullOrEmpty(match.MetaTemplateId)) match.MetaTemplateId = r.Id;
                if (!string.IsNullOrWhiteSpace(r.Category)) match.Category = r.Category.ToUpperInvariant();
                if (!string.IsNullOrWhiteSpace(r.BodyText)) match.BodyText = r.BodyText!;
                match.HeaderText = r.HeaderText;
                match.HeaderType = string.IsNullOrWhiteSpace(r.HeaderText) ? null : "TEXT";
                match.FooterText = r.FooterText;
                match.LastSyncedAt = DateTime.UtcNow;
                match.UpdatedAt = DateTime.UtcNow;
                updated++;
            }
        }

        await db.SaveChangesAsync(ct);

        var all = await db.MetaMessageTemplates
            .Where(t => t.TenantId == tenantId && t.WhatsAppLineId == line.Id)
            .OrderByDescending(t => t.CreatedAt).ThenBy(t => t.SequenceOrder)
            .ToListAsync(ct);
        return Ok(new { imported, updated, templates = all.Select(Project) });
    }

    private static string NormalizeStatus(string? status) =>
        string.IsNullOrWhiteSpace(status) ? MetaTemplateStatuses.Pending : status.ToUpperInvariant();

    // ── BORRAR (acá y en Meta) ───────────────────────────────────────────
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;
        var entity = await db.MetaMessageTemplates
            .FirstOrDefaultAsync(t => t.Id == id && t.TenantId == tenantId, ct);
        if (entity is null) return NotFound();

        // Best-effort: borrar en Meta si ya fue enviada.
        if (entity.MetaStatus != MetaTemplateStatuses.Draft)
        {
            var (line, _) = await ResolveMetaLineAsync(entity.WhatsAppLineId, tenantId, ct);
            if (line is not null)
            {
                var del = await metaTemplates.DeleteAsync(line.MetaWabaId!, line.MetaAccessToken!, entity.Name, ct);
                if (!del.Success)
                    return BadRequest(new { error = $"No se pudo borrar en Meta: {del.Error}. La plantilla no se eliminó." });
            }
        }

        db.MetaMessageTemplates.Remove(entity);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ────────────────────────── Helpers ──────────────────────────────────

    /// <summary>
    /// Resuelve la línea filtrando por el TenantId del JWT y valida que sea Meta con
    /// WABA + token. Devuelve un error HTTP listo si algo falta (anti-error / por-tenant).
    /// </summary>
    private async Task<(WhatsAppLine? Line, IActionResult? Error)> ResolveMetaLineAsync(
        Guid lineId, Guid tenantId, CancellationToken ct)
    {
        var line = await db.WhatsAppLines
            .FirstOrDefaultAsync(l => l.Id == lineId && l.TenantId == tenantId, ct);

        if (line is null)
            return (null, NotFound(new { error = "Línea no encontrada para este tenant." }));
        if (line.Provider != ProviderType.MetaCloudApi)
            return (null, BadRequest(new { error = "Las plantillas solo aplican a líneas con proveedor Meta Cloud API." }));
        if (string.IsNullOrWhiteSpace(line.MetaWabaId) || string.IsNullOrWhiteSpace(line.MetaAccessToken))
            return (null, BadRequest(new { error = "La línea Meta no tiene WABA ID o Access Token configurado." }));

        return (line, null);
    }

    private static MetaTemplateInput BuildInput(MetaMessageTemplate e)
    {
        var (header, body) = DeserializeSamples(e.VariableSamplesJson);
        return new MetaTemplateInput(
            e.Name, e.Language, e.Category,
            e.HeaderText, e.BodyText, e.FooterText,
            header, body);
    }

    /// <summary>Valida nombre, categoría, cuerpo y que los ejemplos cuadren con {{n}}.</summary>
    private IActionResult? ValidateTemplate(
        string name, string category, string? headerText, string bodyText,
        List<string>? headerSamples, List<string>? bodySamples)
    {
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { error = "El nombre es obligatorio." });
        if (!NameRegex().IsMatch(name))
            return BadRequest(new { error = "El nombre debe ser snake_case (minúsculas, números y guion bajo)." });
        if (!MetaTemplateCategories.IsValid(category))
            return BadRequest(new { error = "Categoría inválida (MARKETING, UTILITY o AUTHENTICATION)." });
        if (string.IsNullOrWhiteSpace(bodyText))
            return BadRequest(new { error = "El cuerpo de la plantilla es obligatorio." });

        var headerVars = CountPlaceholders(headerText);
        if (headerVars > 1)
            return BadRequest(new { error = "El encabezado admite como máximo una variable {{1}}." });
        var headerCount = headerSamples?.Count(s => !string.IsNullOrWhiteSpace(s)) ?? 0;
        if (headerVars != headerCount)
            return BadRequest(new { error = $"El encabezado tiene {headerVars} variable(s) pero {headerCount} ejemplo(s)." });

        var bodyVars = CountPlaceholders(bodyText);
        var bodyCount = bodySamples?.Count(s => !string.IsNullOrWhiteSpace(s)) ?? 0;
        if (bodyVars != bodyCount)
            return BadRequest(new { error = $"El cuerpo tiene {bodyVars} variable(s) {{1}}..{{{bodyVars}}} pero {bodyCount} ejemplo(s)." });

        return null;
    }

    private static int CountPlaceholders(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var matches = PlaceholderRegex().Matches(text);
        var distinct = new HashSet<int>();
        foreach (Match m in matches)
            if (int.TryParse(m.Groups[1].Value, out var n)) distinct.Add(n);
        return distinct.Count;
    }

    private static string NormalizeName(string raw)
    {
        var lowered = (raw ?? "").Trim().ToLowerInvariant();
        lowered = NonNameCharsRegex().Replace(lowered, "_");
        lowered = MultiUnderscoreRegex().Replace(lowered, "_").Trim('_');
        return lowered;
    }

    /// <summary>Serializa el mapeo {{n}}→campo como { "body": ["NombreCliente", ...] }.</summary>
    private static string? SerializeMapping(List<string>? bodyMapping)
    {
        var body = (bodyMapping ?? new()).Select(s => s ?? "").ToList();
        if (body.Count == 0) return null;
        return JsonSerializer.Serialize(new { body });
    }

    private static List<string> DeserializeMapping(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("body", out var arr) && arr.ValueKind == JsonValueKind.Array)
                return arr.EnumerateArray().Select(e => e.GetString() ?? "").ToList();
        }
        catch { }
        return new();
    }

    private static string? SerializeSamples(List<string>? header, List<string>? body)
    {
        var obj = new
        {
            header = (header ?? new()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList(),
            body = (body ?? new()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList(),
        };
        return JsonSerializer.Serialize(obj);
    }

    private static (List<string> Header, List<string> Body) DeserializeSamples(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return (new(), new());
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            List<string> Read(string prop) =>
                root.TryGetProperty(prop, out var arr) && arr.ValueKind == JsonValueKind.Array
                    ? arr.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToList()
                    : new();
            return (Read("header"), Read("body"));
        }
        catch { return (new(), new()); }
    }

    private Guid? CurrentUserId()
    {
        var s = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value
             ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(s, out var g) ? g : null;
    }

    private static object Project(MetaMessageTemplate t)
    {
        var (header, body) = DeserializeSamples(t.VariableSamplesJson);
        return new
        {
            t.Id,
            t.WhatsAppLineId,
            t.Name,
            t.Language,
            t.Category,
            t.HeaderType,
            t.HeaderText,
            t.BodyText,
            t.FooterText,
            HeaderSamples = header,
            BodySamples = body,
            t.MetaTemplateId,
            t.MetaStatus,
            t.MetaRejectedReason,
            t.IsEnabled,
            t.BubbleGroupId,
            t.SequenceOrder,
            t.CampaignTemplateId,
            t.Purpose,
            BodyMapping = DeserializeMapping(t.ParameterMappingJson),
            // Utilizable en campaña (Fase 2): aprobada + habilitada.
            Usable = t.MetaStatus == MetaTemplateStatuses.Approved && t.IsEnabled,
            t.CreatedAt,
            t.UpdatedAt,
            t.LastSyncedAt,
        };
    }

    [GeneratedRegex(@"^[a-z0-9_]+$")]
    private static partial Regex NameRegex();
    [GeneratedRegex(@"\{\{\s*(\d+)\s*\}\}")]
    private static partial Regex PlaceholderRegex();
    [GeneratedRegex(@"[^a-z0-9_]+")]
    private static partial Regex NonNameCharsRegex();
    [GeneratedRegex(@"_+")]
    private static partial Regex MultiUnderscoreRegex();
}
