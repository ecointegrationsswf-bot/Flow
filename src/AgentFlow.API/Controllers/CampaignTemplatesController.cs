using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Email;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.API.Controllers;

public record DuplicateTemplateRequest(string Name);

public record CampaignTemplateRequest(
    string Name, Guid AgentDefinitionId,
    List<int> FollowUpHours, int AutoCloseHours,
    List<Guid> LabelIds,
    bool SendEmail = false, string? EmailAddress = null,
    List<Guid>? ActionIds = null,
    string? ActionConfigs = null,
    List<Guid>? PromptTemplateIds = null,
    string SystemPrompt = "",
    string? SendFrom = null, string? SendUntil = null,
    int MaxRetries = 3, int RetryIntervalHours = 24,
    int InactivityCloseHours = 72, string? CloseConditionKeyword = null,
    int MaxTokens = 1024,
    List<int>? AttentionDays = null,
    string AttentionStartTime = "08:00",
    string AttentionEndTime = "17:00",
    string OutOfContextPolicy = "Contain",
    // Fase 2 — Campaign Automation Worker
    string? FollowUpMessagesJson = null,
    string? FollowUpTemplateIdsJson = null,   // plantillas Meta de seguimiento por índice
    string? AutoCloseMessage = null,
    // Plantilla de correo personalizable (Fase 4 — tab "Correo")
    string? EmailSubject = null,
    string? EmailBodyHtml = null,
    string? EmailBodyText = null,
    // Layout adaptativo del correo (Fase A)
    int UmbralCorporativo = 10,
    string? ItemsConfig = null,
    // Archivo modelo parseado — primera fila con TODOS los campos del JSON
    // que vendrá en runtime. Lo usa el frontend para poblar dropdowns y el
    // backend para renderizar previews con datos reales.
    string? SampleDataJson = null,
    // Fase 3 — el etiquetado IA ya NO se configura aquí. Vive en /admin/scheduled-jobs.
    // Motor de flujos: flujo (TenantFlow) que enmarca las conversaciones de este maestro.
    // null = sin flujo (comportamiento clásico). El binding también es editable desde
    // /admin/workflows (super admin) — ambos escriben el mismo campo.
    Guid? ActiveFlowId = null
);

public record EmailPreviewRequest(string? Subject, string? HtmlBody, string? TextBody, string? ItemsConfig = null, int UmbralCorporativo = 10, string? SampleDataJson = null);
public record EmailTestSendRequest(string ToEmail, string? Subject, string? HtmlBody, string? TextBody, string? ItemsConfig = null, int UmbralCorporativo = 10, string? SampleDataJson = null);

[ApiController]
[Route("api/campaign-templates")]
[Authorize]
public class CampaignTemplatesController(
    ITenantContext tenantCtx,
    AgentFlowDbContext db,
    EmailTemplateRenderer emailRenderer,
    IEmailService emailService,
    AgentFlow.Application.Modules.Campaigns.IFixedFormatCampaignService fixedFormatService) : ControllerBase
{
    /// <summary>
    /// Datos de ejemplo para renderizar previews del template del correo.
    /// Match con los slots del EmailRenderContext — el usuario los ve en el frontend
    /// como "datos de ejemplo" antes de mandar el correo de prueba.
    /// </summary>
    /// <summary>
    /// Construye un EmailRenderContext de muestra. Si <paramref name="sampleDataJson"/>
    /// trae el JSON real del archivo modelo del maestro, lo usa como
    /// <c>ClienteDatosJson</c> (de modo que el preview refleja los campos
    /// reales que el cliente recibirá). Si no, cae a un sample hardcoded
    /// para seguros (María Lucía con 2 pólizas).
    /// </summary>
    private static EmailRenderContext SampleRenderContext(
        string? itemsConfigJson = null, int umbral = 10, string? sampleDataJson = null)
    {
        // Defaults hardcoded — se usan cuando el maestro no tiene archivo modelo.
        const string FALLBACK_JSON =
            "[" +
            "{\"numero\":\"AUT-2024-08471\",\"aseguradora\":\"ASSA\",\"ramo\":\"Auto\",\"saldo\":\"B/. 487.50\",\"marca\":\"Toyota Corolla 2022\",\"placa\":\"1234ABC\",\"vencimiento\":\"30 mayo 2026\",\"cuotas_pendientes\":\"2\"}," +
            "{\"numero\":\"VID-2023-12390\",\"aseguradora\":\"Sura\",\"ramo\":\"Vida\",\"saldo\":\"B/. 215.00\",\"beneficiarios\":\"2\",\"vencimiento\":\"15 junio 2026\",\"cuotas_pendientes\":\"1\"}" +
            "]";
        var datos = !string.IsNullOrWhiteSpace(sampleDataJson) ? sampleDataJson : FALLBACK_JSON;

        return new EmailRenderContext
        {
            ClienteNombre      = "María Lucía Martínez",
            ClienteTelefono    = "+507 6234-5678",
            ClienteEmail       = "maria.martinez@example.com",
            ClientePoliza      = "AUT-2024-08471",
            ClienteAseguradora = "ASSA Compañía de Seguros",
            ClienteSaldo       = "B/. 702.50",
            ClienteDatosJson   = datos,
            ConversacionResumen = "Cliente confirmó compromiso de pago por SINPE para el viernes 23 de mayo.",
            ConversacionMensajesHtml = "<p><b>Cliente:</b> Hola, ¿cuánto debo?</p><p><b>Agente:</b> Tu saldo total es B/. 702.50 en 2 pólizas.</p>",
            ConversacionEstado = "Compromiso de pago",
            CampanaNombre = "Cobros Mayo 2026",
            AgenteNombre  = "Ana",
            TenantNombre  = "Mi Corredor",
            Fecha = DateTime.UtcNow.ToString("dd/MM/yyyy"),
            Hora  = DateTime.UtcNow.ToString("HH:mm"),
            ItemsConfigJson = itemsConfigJson,
            UmbralCorporativo = umbral,
        };
    }

    /// <summary>
    /// Parsea un archivo modelo (Excel/CSV en formato fijo) y devuelve:
    ///   - <c>columns</c>: lista de columnas detectadas (4 fijas + extras)
    ///   - <c>sampleRow</c>: primer registro completo como objeto JSON
    ///   - <c>sampleDataJson</c>: array JSON con TODOS los registros del primer
    ///     contacto (formato idéntico al ContactDataJson que produce el
    ///     dispatcher al lanzar la campaña). Sirve para persistir en el maestro
    ///     y usarlo en previews.
    /// El usuario sube acá su archivo modelo desde el tab Correo del maestro,
    /// y los dropdowns de mapeo se alimentan con las columnas reales.
    /// </summary>
    [HttpPost("email/parse-sample")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public IActionResult ParseEmailSample(IFormFile file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "Archivo vacío." });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not ".xlsx" and not ".xls" and not ".csv")
            return BadRequest(new { error = "Formato no soportado. Use Excel (.xlsx/.xls) o CSV." });

        AgentFlow.Application.Modules.Campaigns.FixedFormatParseResult parsed;
        using (var stream = file.OpenReadStream())
            parsed = fixedFormatService.Parse(stream, file.FileName);

        if (parsed.Contacts.Count == 0)
            return BadRequest(new
            {
                error = "No se pudo extraer ningún contacto del archivo.",
                warnings = parsed.Warnings,
            });

        var firstContact = parsed.Contacts[0];
        var sampleDataJson = firstContact.ContactDataJson;

        // Extraer las columnas del primer registro (claves del primer objeto del array).
        var columns = new List<string>();
        object? firstRow = null;
        if (!string.IsNullOrWhiteSpace(sampleDataJson))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(sampleDataJson);
                if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array
                    && doc.RootElement.GetArrayLength() > 0)
                {
                    var first = doc.RootElement[0];
                    if (first.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        foreach (var p in first.EnumerateObject())
                            columns.Add(p.Name);
                        firstRow = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(first.GetRawText());
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ParseEmailSample] No se pudo parsear sample json: {ex.Message}");
            }
        }

        return Ok(new
        {
            columns,
            sampleRow = firstRow,
            sampleDataJson,
            totalContacts = parsed.Contacts.Count,
            totalRowsRead = parsed.TotalRowsRead,
            extraColumns = parsed.ExtraColumns,
            warnings = parsed.Warnings,
        });
    }

    /// <summary>
    /// Renderiza la plantilla con datos de ejemplo. No envía nada — devuelve el
    /// asunto y HTML resueltos para que el frontend los muestre en un modal preview.
    /// </summary>
    [HttpPost("email/preview")]
    public IActionResult EmailPreview([FromBody] EmailPreviewRequest req)
    {
        var ctx = SampleRenderContext(req.ItemsConfig, req.UmbralCorporativo, req.SampleDataJson);
        var rendered = emailRenderer.Render(req.Subject, req.HtmlBody, req.TextBody, ctx);
        return Ok(new { subject = rendered.Subject, htmlBody = rendered.HtmlBody, textBody = rendered.TextBody });
    }

    /// <summary>
    /// Envía un correo de prueba al email indicado con datos de ejemplo.
    /// Útil para que el admin valide en su bandeja real cómo se ve en Gmail/Outlook
    /// antes de lanzar la campaña.
    /// </summary>
    [HttpPost("email/test-send")]
    public async Task<IActionResult> EmailTestSend([FromBody] EmailTestSendRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.ToEmail))
            return BadRequest(new { error = "Falta el email destinatario." });

        var ctx = SampleRenderContext(req.ItemsConfig, req.UmbralCorporativo, req.SampleDataJson);
        var rendered = emailRenderer.Render(req.Subject, req.HtmlBody, req.TextBody, ctx);

        var subject = string.IsNullOrWhiteSpace(rendered.Subject)
            ? "[Prueba] Plantilla de correo"
            : $"[Prueba] {rendered.Subject}";

        try
        {
            await emailService.SendCustomHtmlAsync(
                req.ToEmail.Trim(), ccEmail: null,
                subject, rendered.HtmlBody, rendered.TextBody,
                ct);
            return Ok(new { ok = true, sentTo = req.ToEmail.Trim() });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = $"No se pudo enviar: {ex.Message}" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;
        var templates = await db.CampaignTemplates
            .Where(t => t.TenantId == tenantId)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new
            {
                t.Id, t.Name, t.AgentDefinitionId,
                AgentName = db.AgentDefinitions.Where(a => a.Id == t.AgentDefinitionId).Select(a => a.Name).FirstOrDefault(),
                // Necesario en el frontend para evitar que el dropdown deje seleccionar
                // maestros cuyo agente esté inactivo.
                AgentIsActive = db.AgentDefinitions.Where(a => a.Id == t.AgentDefinitionId).Select(a => (bool?)a.IsActive).FirstOrDefault(),
                t.FollowUpHours, t.FollowUpMessagesJson, t.FollowUpTemplateIdsJson,
                t.AutoCloseHours, t.AutoCloseMessage,
                t.LabelIds,
                t.SendEmail, t.EmailAddress,
                t.ActionIds, t.ActionConfigs, t.PromptTemplateIds,
                t.SystemPrompt, t.SendFrom, t.SendUntil,
                t.MaxRetries, t.RetryIntervalHours, t.InactivityCloseHours,
                t.CloseConditionKeyword, t.MaxTokens,
                t.AttentionDays, t.AttentionStartTime, t.AttentionEndTime,
                t.EmailSubject, t.EmailBodyHtml, t.EmailBodyText, t.EmailTemplateUpdatedAt,
                t.UmbralCorporativo, t.ItemsConfig, t.SampleDataJson,
                t.IsActive, t.CreatedAt, t.UpdatedAt
            })
            .ToListAsync(ct);

        return Ok(templates);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;
        var t = await db.CampaignTemplates
            .Where(x => x.Id == id && x.TenantId == tenantId)
            .Select(x => new
            {
                x.Id, x.Name, x.AgentDefinitionId,
                AgentName = db.AgentDefinitions.Where(a => a.Id == x.AgentDefinitionId).Select(a => a.Name).FirstOrDefault(),
                x.FollowUpHours, x.FollowUpMessagesJson, x.FollowUpTemplateIdsJson,
                x.AutoCloseHours, x.AutoCloseMessage,
                x.LabelIds,
                x.SendEmail, x.EmailAddress,
                x.ActionIds, x.ActionConfigs, x.PromptTemplateIds,
                x.SystemPrompt, x.SendFrom, x.SendUntil,
                x.MaxRetries, x.RetryIntervalHours, x.InactivityCloseHours,
                x.CloseConditionKeyword, x.MaxTokens,
                x.AttentionDays, x.AttentionStartTime, x.AttentionEndTime,
                OutOfContextPolicy = x.OutOfContextPolicy.ToString(),
                x.EmailSubject, x.EmailBodyHtml, x.EmailBodyText, x.EmailTemplateUpdatedAt,
                x.UmbralCorporativo, x.ItemsConfig, x.SampleDataJson,
                x.ActiveFlowId,
                x.IsActive, x.CreatedAt, x.UpdatedAt
            })
            .FirstOrDefaultAsync(ct);

        if (t is null) return NotFound();
        return Ok(t);
    }

    /// <summary>
    /// Flujos (workflows del lienzo) del tenant actual — para el selector "Flujo de
    /// conversación" del formulario del maestro. Solo activos, livianos (sin grafo).
    /// </summary>
    [HttpGet("flows")]
    public async Task<IActionResult> GetTenantFlows(CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;
        var flows = await db.TenantFlows.AsNoTracking()
            .Where(f => f.TenantId == tenantId && f.IsActive)
            .OrderBy(f => f.Name)
            .Select(f => new { f.Id, f.Name, f.Description })
            .ToListAsync(ct);
        return Ok(flows);
    }

    /// <summary>
    /// Valida que el flujo (si se mandó) exista y pertenezca al tenant. Un id ajeno o
    /// inexistente se descarta a null (no rompe el guardado del maestro por esto).
    /// </summary>
    private async Task<Guid?> ValidateFlowAsync(Guid? flowId, Guid tenantId, CancellationToken ct)
    {
        if (flowId is not { } fid || fid == Guid.Empty) return null;
        var ok = await db.TenantFlows.AsNoTracking()
            .AnyAsync(f => f.Id == fid && f.TenantId == tenantId, ct);
        return ok ? fid : null;
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CampaignTemplateRequest req,
        [FromQuery] bool confirmSwap,
        CancellationToken ct)
    {
        if (req.ActionConfigs != null && req.ActionConfigs.Length > 50000)
            return BadRequest(new { error = "ActionConfigs excede el tamaño máximo permitido." });

        var tenantId = tenantCtx.TenantId;

        // Validación crítica del SystemPrompt: el CampaignTemplate es la ÚNICA
        // fuente del prompt en el sistema. Sin prompt, el agente responde con
        // el canned + escalación a humano, y la campaña queda inservible. El
        // AgentDefinition NO tiene prompt — solo el template.
        if (string.IsNullOrWhiteSpace(req.SystemPrompt))
            return BadRequest(new
            {
                error = "El maestro debe tener un SystemPrompt — es la única fuente del prompt del agente. Escribilo antes de guardar.",
                field = "systemPrompt",
            });

        // Validación + swap del maestro primario (no-Brain). Si el agente ya
        // tiene un maestro primario y el admin no confirmó el swap, retorna 409.
        var swap = await ResolvePrimarySwapAsync(
            tenantId, req.AgentDefinitionId, excludeTemplateId: null, confirmSwap, ct);
        if (swap.ConflictResult is not null) return swap.ConflictResult;

        var template = new CampaignTemplate
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = req.Name,
            AgentDefinitionId = req.AgentDefinitionId,
            IsPrimaryForAgent = swap.NewTemplateShouldBePrimary,
            FollowUpHours = req.FollowUpHours,
            FollowUpMessagesJson = req.FollowUpMessagesJson,
            FollowUpTemplateIdsJson = req.FollowUpTemplateIdsJson,
            AutoCloseHours = req.AutoCloseHours,
            AutoCloseMessage = req.AutoCloseMessage,
            LabelIds = req.LabelIds,
            SendEmail = req.SendEmail,
            EmailAddress = req.EmailAddress,
            ActionIds = req.ActionIds ?? [],
            ActionConfigs = req.ActionConfigs,
            PromptTemplateIds = req.PromptTemplateIds ?? [],
            SystemPrompt = req.SystemPrompt,
            SendFrom = req.SendFrom,
            SendUntil = req.SendUntil,
            MaxRetries = req.MaxRetries,
            RetryIntervalHours = req.RetryIntervalHours,
            InactivityCloseHours = req.InactivityCloseHours,
            CloseConditionKeyword = req.CloseConditionKeyword,
            MaxTokens = req.MaxTokens,
            AttentionDays = req.AttentionDays ?? [1, 2, 3, 4, 5],
            AttentionStartTime = req.AttentionStartTime,
            AttentionEndTime = req.AttentionEndTime,
            OutOfContextPolicy = Enum.TryParse<AgentFlow.Domain.Enums.OutOfContextPolicy>(req.OutOfContextPolicy, out var policy)
                ? policy : AgentFlow.Domain.Enums.OutOfContextPolicy.Contain,
            EmailSubject = string.IsNullOrWhiteSpace(req.EmailSubject) ? null : req.EmailSubject,
            EmailBodyHtml = string.IsNullOrWhiteSpace(req.EmailBodyHtml) ? null : req.EmailBodyHtml,
            EmailBodyText = string.IsNullOrWhiteSpace(req.EmailBodyText) ? null : req.EmailBodyText,
            EmailTemplateUpdatedAt = !string.IsNullOrWhiteSpace(req.EmailBodyHtml) ? DateTime.UtcNow : null,
            UmbralCorporativo = req.UmbralCorporativo > 0 ? req.UmbralCorporativo : 10,
            ItemsConfig = string.IsNullOrWhiteSpace(req.ItemsConfig) ? null : req.ItemsConfig,
            SampleDataJson = string.IsNullOrWhiteSpace(req.SampleDataJson) ? null : req.SampleDataJson,
            ActiveFlowId = await ValidateFlowAsync(req.ActiveFlowId, tenantId, ct),
        };

        db.CampaignTemplates.Add(template);
        await db.SaveChangesAsync(ct);
        await EnableKeepAiActiveIfTransferChatAsync(tenantId, template.ActionIds, ct);
        return Ok(new { template.Id, template.Name });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] CampaignTemplateRequest req,
        [FromQuery] bool confirmSwap,
        CancellationToken ct)
    {
        if (req.ActionConfigs != null && req.ActionConfigs.Length > 50000)
            return BadRequest(new { error = "ActionConfigs excede el tamaño máximo permitido." });

        // Misma validación que Create: SystemPrompt obligatorio.
        if (string.IsNullOrWhiteSpace(req.SystemPrompt))
            return BadRequest(new
            {
                error = "El maestro debe tener un SystemPrompt — es la única fuente del prompt del agente. Escribilo antes de guardar.",
                field = "systemPrompt",
            });

        var tenantId = tenantCtx.TenantId;
        var template = await db.CampaignTemplates
            .FirstOrDefaultAsync(t => t.Id == id && t.TenantId == tenantId, ct);

        if (template is null) return NotFound();

        // Si cambió de agente, revalidar el swap del primario para el nuevo agente.
        if (template.AgentDefinitionId != req.AgentDefinitionId)
        {
            var swap = await ResolvePrimarySwapAsync(
                tenantId, req.AgentDefinitionId, excludeTemplateId: id, confirmSwap, ct);
            if (swap.ConflictResult is not null) return swap.ConflictResult;
            template.IsPrimaryForAgent = swap.NewTemplateShouldBePrimary;
        }

        template.Name = req.Name;
        template.AgentDefinitionId = req.AgentDefinitionId;
        template.FollowUpHours = req.FollowUpHours;
        template.FollowUpMessagesJson = req.FollowUpMessagesJson;
        template.FollowUpTemplateIdsJson = req.FollowUpTemplateIdsJson;
        template.AutoCloseHours = req.AutoCloseHours;
        template.AutoCloseMessage = req.AutoCloseMessage;
        template.LabelIds = req.LabelIds;
        template.SendEmail = req.SendEmail;
        template.EmailAddress = req.EmailAddress;
        template.ActionIds = req.ActionIds ?? [];
        template.ActionConfigs = req.ActionConfigs;
        template.PromptTemplateIds = req.PromptTemplateIds ?? [];
        template.SystemPrompt = req.SystemPrompt;
        template.SendFrom = req.SendFrom;
        template.SendUntil = req.SendUntil;
        template.MaxRetries = req.MaxRetries;
        template.RetryIntervalHours = req.RetryIntervalHours;
        template.InactivityCloseHours = req.InactivityCloseHours;
        template.CloseConditionKeyword = req.CloseConditionKeyword;
        template.MaxTokens = req.MaxTokens;
        template.AttentionDays = req.AttentionDays ?? [1, 2, 3, 4, 5];
        template.AttentionStartTime = req.AttentionStartTime;
        template.AttentionEndTime = req.AttentionEndTime;
        template.OutOfContextPolicy = Enum.TryParse<AgentFlow.Domain.Enums.OutOfContextPolicy>(req.OutOfContextPolicy, out var pol)
            ? pol : AgentFlow.Domain.Enums.OutOfContextPolicy.Contain;

        // Email template: si cambió cualquiera de los 3 campos, actualizar timestamp.
        var emailChanged =
            (template.EmailSubject ?? string.Empty)  != (req.EmailSubject ?? string.Empty) ||
            (template.EmailBodyHtml ?? string.Empty) != (req.EmailBodyHtml ?? string.Empty) ||
            (template.EmailBodyText ?? string.Empty) != (req.EmailBodyText ?? string.Empty);
        template.EmailSubject  = string.IsNullOrWhiteSpace(req.EmailSubject)  ? null : req.EmailSubject;
        template.EmailBodyHtml = string.IsNullOrWhiteSpace(req.EmailBodyHtml) ? null : req.EmailBodyHtml;
        template.EmailBodyText = string.IsNullOrWhiteSpace(req.EmailBodyText) ? null : req.EmailBodyText;
        if (emailChanged) template.EmailTemplateUpdatedAt = DateTime.UtcNow;
        template.UmbralCorporativo = req.UmbralCorporativo > 0 ? req.UmbralCorporativo : 10;
        template.ItemsConfig = string.IsNullOrWhiteSpace(req.ItemsConfig) ? null : req.ItemsConfig;
        template.SampleDataJson = string.IsNullOrWhiteSpace(req.SampleDataJson) ? null : req.SampleDataJson;
        // Motor de flujos: vínculo maestro→flujo. IMPORTANTE: el form del maestro NO envía este
        // campo — si viene null se PRESERVA el vínculo existente (sin esto, cada "Actualizar" del
        // maestro desvinculaba el flujo silenciosamente). Desvincular se hace desde /admin/workflows.
        if (req.ActiveFlowId is not null)
            template.ActiveFlowId = await ValidateFlowAsync(req.ActiveFlowId, tenantId, ct);

        template.UpdatedAt = DateTime.UtcNow;

        // HasConversion en List<int> no tiene ValueComparer — marcar explícitamente como modificado
        db.Entry(template).Property(t => t.AttentionDays).IsModified = true;
        db.Entry(template).Property(t => t.AttentionStartTime).IsModified = true;
        db.Entry(template).Property(t => t.AttentionEndTime).IsModified = true;

        await db.SaveChangesAsync(ct);
        await EnableKeepAiActiveIfTransferChatAsync(tenantId, template.ActionIds, ct);
        return Ok(new { template.Id, template.Name });
    }

    /// <summary>
    /// Escalamiento robusto: si el maestro tiene TRANSFER_CHAT vinculada, auto-habilita el
    /// no-silencio del tenant (<see cref="AgentFlow.Domain.Entities.Tenant.KeepAiActiveUntilTakeover"/>).
    /// Así configurar la transferencia a humano ACTIVA el comportamiento — no es una opción aparte
    /// que recordar. No se apaga al quitar TRANSFER_CHAT (sin la acción el flag es inofensivo, y un
    /// tenant pudo haberlo querido explícito).
    /// </summary>
    private async Task EnableKeepAiActiveIfTransferChatAsync(Guid tenantId, List<Guid> actionIds, CancellationToken ct)
    {
        if (actionIds is null || actionIds.Count == 0) return;
        var transferId = await db.ActionDefinitions
            .Where(a => a.Name == "TRANSFER_CHAT" && (a.TenantId == tenantId || a.TenantId == null))
            .Select(a => a.Id)
            .FirstOrDefaultAsync(ct);
        if (transferId == Guid.Empty || !actionIds.Contains(transferId)) return;
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is not null && !tenant.KeepAiActiveUntilTakeover)
        {
            tenant.KeepAiActiveUntilTakeover = true;
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Lista las acciones activas asignadas explícitamente a este tenant por el super admin
    /// (Tenant.AssignedActionIds). Sin fallback: si el admin no asignó nada, devuelve vacío.
    /// La validación 409 al desasignar (SuperAdminController) protege el caso de maestros
    /// en uso, así que en flujo normal nunca habrá un template referenciando un ID no asignado.
    ///
    /// AISLAMIENTO por tenant (mayo-2026): si una acción asignada es GLOBAL (TenantId=NULL)
    /// y existe un clon tenant-specific con el mismo Name, devolvemos DefaultWebhookContract
    /// y DefaultTriggerConfig del clon (no de la global). Así el editor del maestro ve el
    /// contract REAL que va a ejecutar el runtime — sino mostraría vacío cuando los contracts
    /// están en el clon. Coherente con TenantActionsController.GetAll.
    /// </summary>
    [HttpGet("available-actions")]
    public async Task<IActionResult> AvailableActions(CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;

        var tenant = await db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.AssignedActionIds })
            .FirstOrDefaultAsync(ct);

        var assignedIds = tenant?.AssignedActionIds ?? [];
        if (assignedIds.Count == 0)
            return Ok(Array.Empty<object>());

        // Cargar las acciones asignadas (pueden ser globales o tenant-specific).
        var assignedActions = await db.Set<ActionDefinition>()
            .Where(a => a.IsActive && assignedIds.Contains(a.Id))
            .OrderBy(a => a.Name)
            .ToListAsync(ct);
        if (assignedActions.Count == 0)
            return Ok(Array.Empty<object>());

        // Cargar TODOS los clones tenant-specific en UN solo round-trip para sobreescribir
        // contract/triggerConfig cuando la fila asignada es global y existe un clon.
        var assignedNames = assignedActions.Select(a => a.Name).Distinct().ToList();
        var clonesByName = await db.Set<ActionDefinition>()
            .Where(a => a.TenantId == tenantId && a.IsActive && assignedNames.Contains(a.Name))
            .ToDictionaryAsync(a => a.Name, ct);

        // Contratos POR TENANT (arquitectura mayo-2026, TenantActionContract): es lo que el
        // runtime resuelve PRIMERO. Sin esto, una acción cuyo contrato vive solo per-tenant
        // aparecía sin contrato en el editor del maestro y la validación del form bloqueaba
        // el guardado exigiendo config legacy (incidente PASESA 2026-06-12).
        var tenantContracts = await db.TenantActionContracts.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.IsActive && assignedIds.Contains(c.ActionDefinitionId))
            .ToDictionaryAsync(c => c.ActionDefinitionId, c => c.ContractJson, ct);

        var result = assignedActions.Select(a =>
        {
            // Si la asignada es global y hay clon tenant-specific con mismo Name,
            // tomamos contract/triggerConfig del clon (lo que ejecuta el runtime).
            var effective = (a.TenantId is null && clonesByName.TryGetValue(a.Name, out var clone))
                ? clone
                : a;
            // Orden de resolución igual al runtime: TenantActionContract → clon/global.
            var contract = tenantContracts.TryGetValue(a.Id, out var tc) && !string.IsNullOrWhiteSpace(tc)
                ? tc
                : effective.DefaultWebhookContract;
            return new
            {
                a.Id,
                a.Name,
                a.Description,
                a.RequiresWebhook,
                a.SendsEmail,
                a.SendsSms,
                DefaultTriggerConfig   = effective.DefaultTriggerConfig,
                DefaultWebhookContract = contract,
            };
        });

        return Ok(result);
    }

    /// <summary>
    /// Lista los prompt templates activos asignados explícitamente a este tenant
    /// por el super admin (Tenant.AssignedPromptIds). Sin fallback.
    /// </summary>
    [HttpGet("available-prompts")]
    public async Task<IActionResult> AvailablePrompts(CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;
        var tenant = await db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.AssignedPromptIds })
            .FirstOrDefaultAsync(ct);

        var assignedIds = tenant?.AssignedPromptIds ?? [];
        if (assignedIds.Count == 0)
            return Ok(Array.Empty<object>());

        var prompts = await db.Set<PromptTemplate>()
            .Where(p => p.IsActive && assignedIds.Contains(p.Id))
            .OrderBy(p => p.Name)
            .Select(p => new { p.Id, p.Name, p.Description, CategoryName = p.Category != null ? p.Category.Name : null })
            .ToListAsync(ct);

        return Ok(prompts);
    }

    /// <summary>
    /// Devuelve el texto completo (SystemPrompt) de un prompt template para que la UI
    /// del maestro pueda copiarlo al campo editable local (CampaignTemplate.SystemPrompt).
    /// Sólo accesible si está asignado al tenant.
    /// </summary>
    [HttpGet("available-prompts/{id:guid}")]
    public async Task<IActionResult> AvailablePromptDetail(Guid id, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;
        var tenant = await db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.AssignedPromptIds })
            .FirstOrDefaultAsync(ct);

        var assignedIds = tenant?.AssignedPromptIds ?? [];
        if (!assignedIds.Contains(id))
            return NotFound(new { error = "Prompt no asignado a este tenant." });

        var prompt = await db.Set<PromptTemplate>()
            .Where(p => p.IsActive && p.Id == id)
            .Select(p => new { p.Id, p.Name, p.Description, p.SystemPrompt })
            .FirstOrDefaultAsync(ct);

        if (prompt is null) return NotFound();
        return Ok(prompt);
    }

    /// <summary>Duplica un maestro de campaña existente con un nuevo nombre.</summary>
    [HttpPost("{id:guid}/duplicate")]
    public async Task<IActionResult> Duplicate(Guid id, [FromBody] DuplicateTemplateRequest req, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;
        var original = await db.CampaignTemplates
            .FirstOrDefaultAsync(t => t.Id == id && t.TenantId == tenantId, ct);

        if (original is null) return NotFound();

        // El duplicado nace SIEMPRE como secundario — preserva al original como
        // primario. Si el admin quiere que el duplicado sea el primario, debe
        // editar el duplicado y confirmar el swap explícitamente.
        var copy = new CampaignTemplate
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = req.Name.Trim(),
            AgentDefinitionId = original.AgentDefinitionId,
            IsPrimaryForAgent = false,
            FollowUpHours = [.. original.FollowUpHours],
            FollowUpMessagesJson = original.FollowUpMessagesJson,
            FollowUpTemplateIdsJson = original.FollowUpTemplateIdsJson,
            AutoCloseHours = original.AutoCloseHours,
            AutoCloseMessage = original.AutoCloseMessage,
            LabelIds = [.. original.LabelIds],
            SendEmail = original.SendEmail,
            EmailAddress = original.EmailAddress,
            ActionIds = [.. original.ActionIds],
            ActionConfigs = original.ActionConfigs,
            PromptTemplateIds = [.. original.PromptTemplateIds],
            SystemPrompt = original.SystemPrompt,
            SendFrom = original.SendFrom,
            SendUntil = original.SendUntil,
            MaxRetries = original.MaxRetries,
            RetryIntervalHours = original.RetryIntervalHours,
            InactivityCloseHours = original.InactivityCloseHours,
            CloseConditionKeyword = original.CloseConditionKeyword,
            MaxTokens = original.MaxTokens,
            AttentionDays = [.. original.AttentionDays],
            AttentionStartTime = original.AttentionStartTime,
            AttentionEndTime = original.AttentionEndTime,
            EmailSubject  = original.EmailSubject,
            EmailBodyHtml = original.EmailBodyHtml,
            EmailBodyText = original.EmailBodyText,
            EmailTemplateUpdatedAt = original.EmailTemplateUpdatedAt,
            UmbralCorporativo = original.UmbralCorporativo,
            ItemsConfig = original.ItemsConfig,
            SampleDataJson = original.SampleDataJson,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        db.CampaignTemplates.Add(copy);
        await db.SaveChangesAsync(ct);
        await EnableKeepAiActiveIfTransferChatAsync(tenantId, copy.ActionIds, ct);
        return Ok(new { copy.Id, copy.Name });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;
        var template = await db.CampaignTemplates
            .FirstOrDefaultAsync(t => t.Id == id && t.TenantId == tenantId, ct);

        if (template is null) return NotFound();

        // Bloqueamos el delete si hay campañas vinculadas. Permitimos al cliente
        // ver el listado de las primeras 10 y proponer la alternativa: inactivar.
        var totalLinked = await db.Campaigns.CountAsync(c => c.CampaignTemplateId == id, ct);
        if (totalLinked > 0)
        {
            var sample = await db.Campaigns
                .Where(c => c.CampaignTemplateId == id)
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => new { c.Id, c.Name, status = c.Status.ToString(), c.CreatedAt })
                .Take(10)
                .ToListAsync(ct);
            return Conflict(new
            {
                error = $"No se puede eliminar el maestro: está vinculado a {totalLinked} campaña{(totalLinked == 1 ? "" : "s")}.",
                totalCampaigns = totalLinked,
                campaigns = sample,
                suggestion = "deactivate",
                templateName = template.Name,
            });
        }

        db.CampaignTemplates.Remove(template);
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "Maestro eliminado." });
    }

    /// <summary>
    /// Inactiva un maestro sin borrarlo. Lo deja IsActive=false y limpia el flag
    /// IsPrimaryForAgent para que no se siga eligiendo como primario. Las
    /// campañas existentes vinculadas siguen funcionando con la config actual.
    /// </summary>
    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;
        var template = await db.CampaignTemplates
            .FirstOrDefaultAsync(t => t.Id == id && t.TenantId == tenantId, ct);
        if (template is null) return NotFound();

        template.IsActive = false;
        template.IsPrimaryForAgent = false;
        template.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return Ok(new { ok = true, message = "Maestro inactivado.", template.Id });
    }

    /// <summary>
    /// Reactiva un maestro inactivo. Si el agente NO tiene otro primario activo,
    /// este se promueve a primario (queda usable de inmediato); si ya hay un
    /// primario, se activa como secundario (promoverlo se hace editándolo, con la
    /// modal de swap). No pasa por ResolvePrimarySwapAsync para no forzar un 409
    /// en un simple "encender".
    /// </summary>
    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;
        var template = await db.CampaignTemplates
            .FirstOrDefaultAsync(t => t.Id == id && t.TenantId == tenantId, ct);
        if (template is null) return NotFound();

        var hasPrimary = await db.CampaignTemplates.AnyAsync(t =>
            t.TenantId == tenantId
            && t.AgentDefinitionId == template.AgentDefinitionId
            && t.Id != template.Id
            && t.IsActive
            && t.IsPrimaryForAgent, ct);

        template.IsActive = true;
        template.IsPrimaryForAgent = !hasPrimary;
        template.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return Ok(new
        {
            ok = true,
            message = template.IsPrimaryForAgent
                ? "Maestro activado como primario del agente."
                : "Maestro activado (el agente ya tiene otro primario).",
            template.Id,
            isPrimaryForAgent = template.IsPrimaryForAgent,
        });
    }

    /// <summary>
    /// Helper de Create/Update — decide si el nuevo/editado maestro puede ser
    /// primario para el agente. Reglas:
    ///   • Si el tenant tiene BrainEnabled=true → no hay primario obligatorio;
    ///     dejamos IsPrimaryForAgent=false (el Cerebro elige por slug).
    ///   • Si el agente NO tiene primario activo → este pasa a ser primario.
    ///   • Si el agente YA tiene un primario activo:
    ///       - Sin confirmSwap → 409 con detalles del maestro actual para que el
    ///         frontend muestre la modal de confirmación.
    ///       - Con confirmSwap=true → bajamos al primario actual a no-primario
    ///         (sólo el flag, sin desactivar) y este pasa a primario.
    /// </summary>
    private async Task<(IActionResult? ConflictResult, bool NewTemplateShouldBePrimary)>
        ResolvePrimarySwapAsync(
            Guid tenantId, Guid agentDefinitionId, Guid? excludeTemplateId,
            bool confirmSwap, CancellationToken ct)
    {
        var brainEnabled = await db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => t.BrainEnabled)
            .FirstOrDefaultAsync(ct);

        if (brainEnabled)
        {
            // Brain decide por slug — el flag IsPrimaryForAgent no se usa.
            return (null, NewTemplateShouldBePrimary: false);
        }

        var currentPrimaryQuery = db.CampaignTemplates
            .Where(t => t.TenantId == tenantId
                     && t.AgentDefinitionId == agentDefinitionId
                     && t.IsActive
                     && t.IsPrimaryForAgent);

        if (excludeTemplateId.HasValue)
            currentPrimaryQuery = currentPrimaryQuery.Where(t => t.Id != excludeTemplateId.Value);

        var currentPrimary = await currentPrimaryQuery.FirstOrDefaultAsync(ct);

        if (currentPrimary is null)
        {
            // El agente no tiene primario activo → el nuevo asume el rol.
            return (null, NewTemplateShouldBePrimary: true);
        }

        // Hay otro primario. Pedimos confirmación o aplicamos swap.
        if (!confirmSwap)
        {
            var conflict = Conflict(new
            {
                error = "primary_template_swap_required",
                message = $"El agente ya tiene un maestro primario asignado: \"{currentPrimary.Name}\". "
                        + "Si continúas, ese maestro perderá el rol primario y este pasará a ser el que "
                        + "responde a los mensajes orgánicos (sin campaña activa). Las campañas vivas "
                        + "del maestro anterior seguirán funcionando sin cambios.",
                currentPrimaryId = currentPrimary.Id,
                currentPrimaryName = currentPrimary.Name,
                hint = "Reintenta con ?confirmSwap=true para aplicar el cambio."
            });
            return (conflict, false);
        }

        // confirmSwap=true → swap atómico (todo en el mismo SaveChanges del caller).
        currentPrimary.IsPrimaryForAgent = false;
        currentPrimary.UpdatedAt = DateTime.UtcNow;
        return (null, NewTemplateShouldBePrimary: true);
    }
}
