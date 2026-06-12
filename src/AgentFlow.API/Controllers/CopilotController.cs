using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.API.Controllers;

/// <summary>
/// Copiloto IT — Fase 1 (Monitor + Prompts). Chat con tool-use de Claude para el equipo
/// super-admin: diagnostica procesos (crons, descargas, campañas, contratos) con herramientas
/// de SOLO LECTURA y propone prompts como BORRADOR que el humano aplica con un clic.
///
/// Seguridad por construcción:
///  - El modelo solo puede invocar las herramientas de este allowlist — no existe ninguna
///    herramienta de SQL crudo ni DDL: no puede tocar estructura de BD.
///  - Toda ESCRITURA es two-step: la tool genera un draft → la UI muestra "Aplicar" → recién
///    el endpoint /apply persiste (con las mismas validaciones del portal).
///  - Secretos (authValue/api keys) SIEMPRE enmascarados en las lecturas.
///  - Corre con el JWT super_admin del usuario; cada apply queda auditado en SystemAuditLog.
/// </summary>
[ApiController]
[Route("api/admin/copilot")]
[Authorize(Roles = "super_admin")]
public class CopilotController(
    AgentFlowDbContext db,
    AnthropicClient anthropic,
    IHttpClientFactory httpFactory,
    AgentFlow.Domain.Interfaces.IDelinquencyProcessor delinquencyProcessor,
    MediatR.IMediator mediator,
    ILogger<CopilotController> logger) : ControllerBase
{
    public record ChatMessageDto(string Role, string Content);
    public record ChatRequest(List<ChatMessageDto> Messages);
    public record ToolActivityDto(string Tool, string Args);
    public record DraftDto(string Type, string Title, JsonObject Payload);
    public record ChatResponse(string Reply, List<ToolActivityDto> Tools, List<DraftDto> Drafts);
    public record ApplyRequest(string Type, JsonObject Payload);

    private const string Model = "claude-sonnet-4-6";
    private const int MaxToolRounds = 10;

    // ─────────────────────────────── CHAT ───────────────────────────────

    [HttpPost("chat")]
    public async Task<ActionResult<ChatResponse>> Chat([FromBody] ChatRequest req, CancellationToken ct)
    {
        if (req.Messages is not { Count: > 0 })
            return BadRequest(new { error = "messages requerido" });

        var messages = new List<Message>();
        foreach (var m in req.Messages.TakeLast(30))
        {
            messages.Add(new Message
            {
                Role = string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase) ? RoleType.Assistant : RoleType.User,
                Content = [new TextContent { Text = m.Content ?? string.Empty }]
            });
        }

        var activity = new List<ToolActivityDto>();
        var drafts = new List<DraftDto>();

        var parameters = new MessageParameters
        {
            Model = Model,
            MaxTokens = 2048,
            System = [new SystemMessage(BuildSystemPrompt())],
            Messages = messages,
            Stream = false,
            Temperature = 0.2m,
            Tools = BuildTools(),
        };

        string reply = string.Empty;
        for (var round = 0; round < MaxToolRounds; round++)
        {
            var res = await anthropic.Messages.GetClaudeMessageAsync(parameters, ct);

            var toolUses = res.Content.OfType<ToolUseContent>().ToList();
            var text = string.Join("\n", res.Content.OfType<TextContent>().Select(t => t.Text));
            if (!string.IsNullOrWhiteSpace(text)) reply = text;

            if (toolUses.Count == 0)
                break;

            // Apendar el turno del asistente tal cual (texto + tool_use) y responder cada tool.
            messages.Add(res.Message);
            var results = new List<ContentBase>();
            foreach (var tu in toolUses)
            {
                var argsJson = tu.Input?.ToJsonString() ?? "{}";
                activity.Add(new ToolActivityDto(tu.Name, Truncate(argsJson, 300)));
                string resultJson;
                try
                {
                    resultJson = await ExecuteToolAsync(tu.Name, tu.Input as JsonObject ?? [], drafts, ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "[Copilot] Tool {Tool} falló", tu.Name);
                    resultJson = JsonSerializer.Serialize(new { error = ex.Message });
                }
                results.Add(new ToolResultContent
                {
                    ToolUseId = tu.Id,
                    Content = [new TextContent { Text = Truncate(resultJson, 30000) }]
                });
            }
            messages.Add(new Message { Role = RoleType.User, Content = results });
        }

        return Ok(new ChatResponse(
            string.IsNullOrWhiteSpace(reply) ? "(sin respuesta)" : reply,
            activity, drafts));
    }

    // ─────────────────────────────── APPLY ──────────────────────────────

    /// <summary>Aplica un borrador generado por el copiloto. ÚNICA vía de escritura, y solo tipos whitelisteados.</summary>
    [HttpPost("apply")]
    public async Task<IActionResult> Apply([FromBody] ApplyRequest req, CancellationToken ct)
    {
        switch (req.Type)
        {
            case "prompt":
                return await ApplyPromptAsync(req.Payload, ct);
            case "action":
                return await ApplyActionAsync(req.Payload, ct);
            case "contract":
                return await ApplyContractAsync(req.Payload, ct);
            case "download_config":
                return await ApplyDownloadConfigAsync(req.Payload, ct);
            case "cron":
                return await ApplyCronAsync(req.Payload, ct);
            case "run_download":
                return await ApplyRunDownloadAsync(req.Payload, ct);
            case "campaign":
                return await ApplyCampaignAsync(req.Payload, ct);
            default:
                return BadRequest(new { error = $"Tipo de borrador no soportado: {req.Type}" });
        }
    }

    private async Task<IActionResult> ApplyCronAsync(JsonObject p, CancellationToken ct)
    {
        Guid? jobId = Guid.TryParse(p["jobId"]?.ToString(), out var jg) ? jg : null;
        var cronExpr = p["cronExpression"]?.ToString();
        Domain.Entities.ScheduledWebhookJob job;
        var notes = new List<string>();

        if (jobId is { } jid)
        {
            job = await db.ScheduledWebhookJobs.FirstOrDefaultAsync(j => j.Id == jid, ct)
                ?? throw new InvalidOperationException("el job no existe");
        }
        else
        {
            if (!Guid.TryParse(p["actionId"]?.ToString(), out var actionId))
                return BadRequest(new { error = "actionId requerido para crear" });
            job = new Domain.Entities.ScheduledWebhookJob
            {
                Id = Guid.NewGuid(),
                ActionDefinitionId = actionId,
                TriggerType = "Cron",
                Scope = p["scope"]?.ToString() ?? "AllTenants",
                ContextId = p["contextId"]?.ToString(),
                IsActive = false, // se activa abajo si vino isActive=true
                CreatedAt = DateTime.UtcNow,
            };
            db.ScheduledWebhookJobs.Add(job);
            notes.Add("job creado");
        }

        if (!string.IsNullOrWhiteSpace(cronExpr))
        {
            job.CronExpression = cronExpr;
            job.TriggerType = "Cron";
            notes.Add($"cron = {cronExpr} (hora Panamá)");
        }
        if (!string.IsNullOrWhiteSpace(p["scope"]?.ToString())) job.Scope = p["scope"]!.ToString();
        if (!string.IsNullOrWhiteSpace(p["contextId"]?.ToString())) job.ContextId = p["contextId"]!.ToString();
        if (bool.TryParse(p["isActive"]?.ToString(), out var act))
        {
            job.IsActive = act;
            notes.Add(act ? "ACTIVADO" : "PAUSADO");
        }

        // Recalcular SIEMPRE el NextRunAt cuando hay cron (regla de oro de la skill scheduled-jobs).
        var runNow = bool.TryParse(p["runNow"]?.ToString(), out var rn) && rn;
        if (runNow)
        {
            job.NextRunAt = DateTime.UtcNow;
            job.LastRunStatus = null;
            notes.Add("ejecución inmediata programada (próximo tick ≤60s)");
        }
        else if (!string.IsNullOrWhiteSpace(job.CronExpression))
        {
            try
            {
                var next = Cronos.CronExpression.Parse(job.CronExpression)
                    .GetNextOccurrence(DateTime.UtcNow, AgentFlow.Infrastructure.ScheduledJobs.PanamaTimeZone.Instance);
                job.NextRunAt = next;
                notes.Add($"próxima corrida {next:yyyy-MM-dd HH:mm} UTC");
            }
            catch { return BadRequest(new { error = $"cron inválida: {job.CronExpression}" }); }
        }
        job.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await AuditAsync("COPILOT_APPLY", $"Job {job.Id.ToString()[..8]}: {string.Join(" · ", notes)}", job.Id.ToString(), ct);
        return Ok(new { jobId = job.Id, notas = notes });
    }

    private async Task<IActionResult> ApplyRunDownloadAsync(JsonObject p, CancellationToken ct)
    {
        if (!Guid.TryParse(p["actionId"]?.ToString(), out var actionId) ||
            !Guid.TryParse(p["tenantId"]?.ToString(), out var tenantId))
            return BadRequest(new { error = "actionId y tenantId requeridos" });

        var config = await db.ActionDelinquencyConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.ActionDefinitionId == actionId && c.TenantId == tenantId && c.IsActive, ct);
        if (config is null) return BadRequest(new { error = "no hay config de descarga activa" });
        var url = config.DownloadWebhookUrl;
        if (string.IsNullOrWhiteSpace(url)) return BadRequest(new { error = "la config no tiene URL" });

        // Misma mecánica que AdminMorosidadController.RunDownloadNow.
        string json;
        var client = httpFactory.CreateClient("delinquency");
        using (var reqMsg = new HttpRequestMessage(
            string.Equals(config.DownloadWebhookMethod, "POST", StringComparison.OrdinalIgnoreCase) ? HttpMethod.Post : HttpMethod.Get, url))
        {
            if (!string.IsNullOrWhiteSpace(config.DownloadWebhookHeaders))
            {
                try
                {
                    var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(config.DownloadWebhookHeaders);
                    if (headers is not null)
                        foreach (var (k, v) in headers) reqMsg.Headers.TryAddWithoutValidation(k, v);
                }
                catch { /* headers mal formados → sin headers extra */ }
            }
            using var resp = await client.SendAsync(reqMsg, ct);
            resp.EnsureSuccessStatusCode();
            json = await resp.Content.ReadAsStringAsync(ct);
        }
        if (string.IsNullOrWhiteSpace(json)) return BadRequest(new { error = "el endpoint devolvió vacío" });

        var executionId = await delinquencyProcessor.ProcessAsync(tenantId, actionId, json, scheduledJobId: null, ct: ct);
        var exec = await db.DelinquencyExecutions.AsNoTracking().Where(e => e.Id == executionId)
            .Select(e => new { e.Status, e.TotalItems, e.ProcessedItems, e.DiscardedItems, e.GroupsCreated, e.CampaignsCreated })
            .FirstOrDefaultAsync(ct);
        await AuditAsync("COPILOT_APPLY", $"Descarga inmediata acción {actionId} tenant {tenantId}: {exec?.TotalItems} ítems", executionId.ToString(), ct);
        return Ok(new { executionId, ejecucion = exec });
    }

    private async Task<IActionResult> ApplyCampaignAsync(JsonObject p, CancellationToken ct)
    {
        if (!Guid.TryParse(p["campaignId"]?.ToString(), out var campaignId))
            return BadRequest(new { error = "campaignId requerido" });
        var op = p["operacion"]?.ToString()?.ToLowerInvariant();
        var camp = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == campaignId, ct);
        if (camp is null) return BadRequest(new { error = "la campaña no existe" });

        string result;
        switch (op)
        {
            case "pausar":
                if (camp.Status != Domain.Enums.CampaignStatus.Running)
                    return BadRequest(new { error = $"solo se pausa una campaña Running (está {camp.Status})" });
                camp.Status = Domain.Enums.CampaignStatus.Paused;
                await db.SaveChangesAsync(ct);
                result = "pausada";
                break;
            case "reanudar":
                if (camp.Status != Domain.Enums.CampaignStatus.Paused)
                    return BadRequest(new { error = $"solo se reanuda una campaña Paused (está {camp.Status})" });
                camp.Status = Domain.Enums.CampaignStatus.Running;
                await db.SaveChangesAsync(ct);
                result = "reanudada — el dispatcher la retoma en su próximo tick";
                break;
            case "lanzar":
                if (camp.Status != Domain.Enums.CampaignStatus.Pending)
                    return BadRequest(new { error = $"solo se lanza una campaña Pending (está {camp.Status})" });
                var user = User?.FindFirst("email")?.Value ?? "copilot";
                var launch = await mediator.Send(new AgentFlow.Application.Modules.Campaigns.LaunchV2.LaunchCampaignV2Command(
                    CampaignId: campaignId,
                    TenantId: camp.TenantId,
                    LaunchedByUserId: $"copilot:{user}",
                    LaunchedByUserPhone: null,
                    WarmupDay: 0), ct);
                if (!launch.Success) return BadRequest(new { error = $"el lanzamiento falló: {launch.Error}" });
                result = $"lanzada — encolados={launch.QueuedCount}, diferidos={launch.DeferredCount}, duplicados={launch.DuplicateCount}";
                break;
            default:
                return BadRequest(new { error = "operacion debe ser pausar | reanudar | lanzar" });
        }
        await AuditAsync("COPILOT_APPLY", $"Campaña '{Truncate(camp.Name, 60)}' {result}", campaignId.ToString(), ct);
        return Ok(new { campaignId, resultado = result });
    }

    private async Task<IActionResult> ApplyActionAsync(JsonObject p, CancellationToken ct)
    {
        var slug = p["name"]?.ToString()?.Trim().ToUpperInvariant();
        var contractJson = (p["contract"] as JsonObject)?.ToJsonString();
        if (string.IsNullOrWhiteSpace(slug) || string.IsNullOrWhiteSpace(contractJson))
            return BadRequest(new { error = "name y contract son requeridos" });
        if (await db.ActionDefinitions.AnyAsync(a => a.Name == slug && a.TenantId == null, ct))
            return BadRequest(new { error = $"ya existe la acción global '{slug}'" });

        var scope = p["contractScope"]?.ToString() ?? "global";
        Guid? tenantId = Guid.TryParse(p["tenantId"]?.ToString(), out var tg) ? tg : null;
        var isDownload = bool.TryParse(p["isDelinquencyDownload"]?.ToString(), out var dd) && dd;

        var action = new Domain.Entities.ActionDefinition
        {
            Id = Guid.NewGuid(),
            TenantId = null,
            Name = slug,
            Description = p["description"]?.ToString(),
            RequiresWebhook = true,
            IsActive = true,
            IsDelinquencyDownload = isDownload,
            DefaultWebhookContract = scope == "global" ? contractJson : null,
            CreatedAt = DateTime.UtcNow,
        };
        db.ActionDefinitions.Add(action);

        var notes = new List<string> { $"acción global {slug} creada" };
        if (scope == "tenant" && tenantId is { } tid1)
        {
            db.TenantActionContracts.Add(new Domain.Entities.TenantActionContract
            {
                Id = Guid.NewGuid(),
                ActionDefinitionId = action.Id,
                TenantId = tid1,
                ContractJson = contractJson,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
            });
            notes.Add("contrato POR TENANT creado");
        }
        if (tenantId is { } tid2)
        {
            var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tid2, ct);
            if (tenant is not null && !tenant.AssignedActionIds.Contains(action.Id))
            {
                tenant.AssignedActionIds = [.. tenant.AssignedActionIds, action.Id];
                notes.Add($"asignada al tenant {tenant.Name}");
            }
        }
        await db.SaveChangesAsync(ct);
        await AuditAsync("COPILOT_APPLY", $"Acción {slug}: {string.Join(" · ", notes)}", action.Id.ToString(), ct);
        return Ok(new { actionId = action.Id, notas = notes, recordatorio = "Registrar el nombre amigable en actionLabels.ts (frontend) en el próximo deploy." });
    }

    private async Task<IActionResult> ApplyContractAsync(JsonObject p, CancellationToken ct)
    {
        if (!Guid.TryParse(p["actionId"]?.ToString(), out var actionId) ||
            !Guid.TryParse(p["tenantId"]?.ToString(), out var tenantId))
            return BadRequest(new { error = "actionId y tenantId son requeridos" });
        var contractJson = (p["contract"] as JsonObject)?.ToJsonString();
        if (string.IsNullOrWhiteSpace(contractJson))
            return BadRequest(new { error = "contract es requerido" });
        if (!await db.ActionDefinitions.AnyAsync(a => a.Id == actionId, ct))
            return BadRequest(new { error = "la acción no existe" });
        if (!await db.Tenants.AnyAsync(t => t.Id == tenantId, ct))
            return BadRequest(new { error = "el tenant no existe" });

        var existing = await db.TenantActionContracts
            .FirstOrDefaultAsync(c => c.ActionDefinitionId == actionId && c.TenantId == tenantId, ct);
        string action;
        if (existing is null)
        {
            db.TenantActionContracts.Add(new Domain.Entities.TenantActionContract
            {
                Id = Guid.NewGuid(),
                ActionDefinitionId = actionId,
                TenantId = tenantId,
                ContractJson = contractJson,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
            });
            action = "creado";
        }
        else
        {
            existing.ContractJson = contractJson;
            existing.IsActive = true;
            existing.UpdatedAt = DateTime.UtcNow;
            action = "actualizado";
        }
        await db.SaveChangesAsync(ct);
        await AuditAsync("COPILOT_APPLY", $"Contrato por tenant {action} (acción {actionId}, tenant {tenantId})", actionId.ToString(), ct);
        return Ok(new { actionId, tenantId, action });
    }

    private async Task<IActionResult> ApplyDownloadConfigAsync(JsonObject p, CancellationToken ct)
    {
        if (!Guid.TryParse(p["actionId"]?.ToString(), out var actionId) ||
            !Guid.TryParse(p["tenantId"]?.ToString(), out var tenantId))
            return BadRequest(new { error = "actionId y tenantId son requeridos" });
        if (p["mappings"] is not JsonArray maps || p["config"] is not JsonObject cfg)
            return BadRequest(new { error = "mappings y config son requeridos" });

        // Mapeos: GLOBALES de la acción (convención: el formato canónico del endpoint se comparte;
        // overrides por tenant se gestionan desde el Módulo de Descargas si hicieran falta).
        var notes = new List<string>();
        var sort = 0;
        foreach (var m in maps.OfType<JsonObject>())
        {
            var columnKey = m["columnKey"]?.ToString();
            if (string.IsNullOrWhiteSpace(columnKey)) continue;
            if (await db.ActionFieldMappings.AnyAsync(x => x.ActionDefinitionId == actionId && x.TenantId == null && x.ColumnKey == columnKey, ct))
            {
                notes.Add($"mapeo '{columnKey}' ya existía (no se tocó)");
                continue;
            }
            db.ActionFieldMappings.Add(new Domain.Entities.ActionFieldMapping
            {
                Id = Guid.NewGuid(),
                ActionDefinitionId = actionId,
                TenantId = null,
                ColumnKey = columnKey,
                JsonPath = m["jsonPath"]?.ToString() ?? $"$.{columnKey}",
                DataType = m["dataType"]?.ToString() ?? "string",
                DisplayName = m["displayName"]?.ToString() ?? columnKey,
                Role = Enum.TryParse<Domain.Enums.FieldRole>(m["role"]?.ToString(), true, out var role) ? role : Domain.Enums.FieldRole.None,
                RoleLabel = m["roleLabel"]?.ToString(),
                DefaultValue = m["defaultValue"]?.ToString(),
                SortOrder = sort++,
                IsEnabled = true,
                CreatedAt = DateTime.UtcNow,
            });
        }

        var existingCfg = await db.ActionDelinquencyConfigs
            .FirstOrDefaultAsync(c => c.ActionDefinitionId == actionId && c.TenantId == tenantId, ct);
        var headersJson = (cfg["downloadWebhookHeaders"] as JsonObject)?.ToJsonString();
        if (existingCfg is null)
        {
            existingCfg = new Domain.Entities.ActionDelinquencyConfig
            {
                Id = Guid.NewGuid(),
                ActionDefinitionId = actionId,
                TenantId = tenantId,
                CodigoPais = "507",
                CreatedAt = DateTime.UtcNow,
            };
            db.ActionDelinquencyConfigs.Add(existingCfg);
            notes.Add("config de descarga creada");
        }
        else notes.Add("config de descarga actualizada");

        existingCfg.DownloadWebhookUrl = cfg["downloadWebhookUrl"]?.ToString();
        existingCfg.DownloadWebhookMethod = cfg["downloadWebhookMethod"]?.ToString() ?? "GET";
        existingCfg.DownloadWebhookHeaders = headersJson;
        existingCfg.ItemsJsonPath = string.IsNullOrWhiteSpace(cfg["itemsJsonPath"]?.ToString()) ? null : cfg["itemsJsonPath"]!.ToString();
        existingCfg.AutoCrearCampanas = bool.TryParse(cfg["autoCrearCampanas"]?.ToString(), out var ac) && ac;
        existingCfg.AutoLaunchCampaigns = bool.TryParse(cfg["autoLaunchCampaigns"]?.ToString(), out var al) && al;
        existingCfg.SplitCampaignsByExecutive = bool.TryParse(cfg["splitCampaignsByExecutive"]?.ToString(), out var sp) && sp;
        existingCfg.CampaignTemplateId = Guid.TryParse(cfg["campaignTemplateId"]?.ToString(), out var ctid) ? ctid : null;
        existingCfg.IsActive = true;
        existingCfg.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        await AuditAsync("COPILOT_APPLY", $"Config de descarga aplicada (acción {actionId}, tenant {tenantId}): {string.Join(" · ", notes)}", actionId.ToString(), ct);
        return Ok(new { actionId, tenantId, notas = notes });
    }

    private async Task<IActionResult> ApplyPromptAsync(JsonObject p, CancellationToken ct)
    {
        var name = p["name"]?.ToString()?.Trim();
        var systemPrompt = p["systemPrompt"]?.ToString();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(systemPrompt))
            return BadRequest(new { error = "name y systemPrompt son requeridos" });

        var description = p["description"]?.ToString();
        var categoryName = p["categoryName"]?.ToString();
        Guid? maestroId = Guid.TryParse(p["maestroId"]?.ToString(), out var mg) ? mg : null;

        Guid? categoryId = null;
        if (!string.IsNullOrWhiteSpace(categoryName))
        {
            categoryId = await db.AgentCategories.AsNoTracking()
                .Where(c => c.Name == categoryName)
                .Select(c => (Guid?)c.Id)
                .FirstOrDefaultAsync(ct);
        }

        var existing = await db.PromptTemplates.FirstOrDefaultAsync(x => x.Name == name, ct);
        Guid promptId;
        string action;
        if (existing is null)
        {
            promptId = Guid.NewGuid();
            db.PromptTemplates.Add(new Domain.Entities.PromptTemplate
            {
                Id = promptId,
                Name = name,
                Description = description,
                CategoryId = categoryId,
                SystemPrompt = systemPrompt,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
            });
            action = "creado";
        }
        else
        {
            existing.Description = description ?? existing.Description;
            if (categoryId is not null) existing.CategoryId = categoryId;
            existing.SystemPrompt = systemPrompt;
            existing.UpdatedAt = DateTime.UtcNow;
            promptId = existing.Id;
            action = "actualizado";
        }
        await db.SaveChangesAsync(ct);

        // Vincular al maestro (opcional): referencia en PromptTemplateIds + copia local del texto,
        // igual que hace el formulario del maestro.
        string? maestroNote = null;
        if (maestroId is { } mid)
        {
            var maestro = await db.CampaignTemplates.FirstOrDefaultAsync(t => t.Id == mid, ct);
            if (maestro is null)
            {
                maestroNote = "maestro no encontrado — prompt guardado sin vincular";
            }
            else
            {
                maestro.PromptTemplateIds = [promptId];
                maestro.SystemPrompt = systemPrompt;
                maestro.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                maestroNote = $"vinculado al maestro '{maestro.Name}' (copia local sincronizada)";
            }
        }

        await AuditAsync("COPILOT_APPLY", $"Prompt '{name}' {action}{(maestroNote is null ? "" : " · " + maestroNote)}", promptId.ToString(), ct);
        return Ok(new { promptId, action, maestroNote });
    }

    // ─────────────────────────── HERRAMIENTAS ───────────────────────────

    private static List<Anthropic.SDK.Common.Tool> BuildTools()
    {
        var tools = new List<Anthropic.SDK.Common.Tool>();
        void Add(string name, string desc, string schemaJson) =>
            tools.Add(new Anthropic.SDK.Common.Function(name, desc, JsonNode.Parse(schemaJson)));

        Add("listar_tenants", "Lista los tenants (corredores) de la plataforma con su id y nombre.",
            """{"type":"object","properties":{}}""");
        Add("listar_acciones", "Lista las acciones globales (catálogo) con su estado, si requieren webhook y si son de descarga. Si pasás tenantId, indica cuáles están asignadas a ese tenant.",
            """{"type":"object","properties":{"tenantId":{"type":"string","description":"GUID del tenant (opcional)"}}}""");
        Add("ver_contrato_accion", "Devuelve el contrato webhook de una acción (URL, método, auth ENMASCARADA, inputSchema, outputSchema, chainRules, triggerConfig). Usa el contrato del tenant si existe, si no el global.",
            """{"type":"object","properties":{"actionId":{"type":"string"},"tenantId":{"type":"string","description":"opcional"}},"required":["actionId"]}""");
        Add("listar_prompts", "Lista los prompts del catálogo global (id, nombre, descripción, categoría, largo, activo).",
            """{"type":"object","properties":{}}""");
        Add("ver_prompt", "Devuelve el texto completo de un prompt del catálogo por id.",
            """{"type":"object","properties":{"promptId":{"type":"string"}},"required":["promptId"]}""");
        Add("listar_maestros", "Lista los maestros de campaña de un tenant con su agente, prompt vinculado, flujo, etiquetas, acciones, horario de atención y ventana de envío.",
            """{"type":"object","properties":{"tenantId":{"type":"string"}},"required":["tenantId"]}""");
        Add("estado_jobs", "Estado de los jobs programados (crons): acción, expresión cron (hora Panamá), próximo run (UTC), último resultado y fallos consecutivos.",
            """{"type":"object","properties":{}}""");
        Add("historial_descargas", "Últimas ejecuciones de descargas (morosidad/avisos): tenant, acción, totales, procesados, descartados, grupos, campañas creadas.",
            """{"type":"object","properties":{"tenantId":{"type":"string","description":"opcional"},"take":{"type":"integer","description":"default 15"}}}""");
        Add("estado_campanas", "Campañas recientes de un tenant con su estado y el desglose de contactos por estado de despacho (Queued/Sent/Failed/Deferred). Útil para diagnosticar por qué una campaña no envía.",
            """{"type":"object","properties":{"tenantId":{"type":"string"},"take":{"type":"integer","description":"default 10"}},"required":["tenantId"]}""");
        Add("errores_recientes", "Errores y eventos del sistema (SystemAuditLog) de las últimas N horas.",
            """{"type":"object","properties":{"horas":{"type":"integer","description":"default 24"}}}""");
        Add("proponer_prompt", "Crea un BORRADOR de prompt (nuevo o edición) para que el usuario lo revise y aplique. NO lo guarda: el usuario debe hacer clic en Aplicar. Si querés editar uno existente, usá el mismo name.",
            """{"type":"object","properties":{"name":{"type":"string"},"description":{"type":"string"},"categoryName":{"type":"string","description":"opcional, ej Cobros"},"systemPrompt":{"type":"string","description":"el texto COMPLETO del prompt"},"maestroId":{"type":"string","description":"opcional: GUID del maestro a vincular"}},"required":["name","systemPrompt"]}""");

        // ── Fase 2: acciones y webhooks desde cero ──
        Add("probar_webhook", "Prueba un endpoint HTTP EN VIVO (como un curl) y devuelve status + body truncado. Usalo SIEMPRE antes de proponer una acción/contrato, para validar URL, auth y forma de la respuesta. Solo http/https públicos; timeout 20s.",
            """{"type":"object","properties":{"url":{"type":"string"},"method":{"type":"string","description":"GET|POST, default GET"},"headers":{"type":"object","description":"headers como objeto clave-valor, ej X-API-Key"},"body":{"type":"string","description":"body JSON crudo para POST (opcional)"}},"required":["url"]}""");
        Add("proponer_accion_webhook", "Crea un BORRADOR de ACCIÓN GLOBAL nueva con su contrato webhook completo, opcionalmente asignada a un tenant. Reglas: slug en MAYUSCULAS_CON_GUIONES_BAJOS y único; probá el endpoint ANTES con probar_webhook; un dataset de descarga nuevo = acción nueva (isDelinquencyDownload=true). El usuario aplica con un clic.",
            """{"type":"object","properties":{"name":{"type":"string","description":"slug UPPER_SNAKE, ej DOWNLOAD_RENEWAL_NOTICES"},"description":{"type":"string"},"isDelinquencyDownload":{"type":"boolean","description":"true si es una descarga de datos (aparece en Módulo de Descargas)"},"tenantId":{"type":"string","description":"GUID del tenant al que asignarla (opcional)"},"contractScope":{"type":"string","description":"'global' (DefaultWebhookContract, default) o 'tenant' (TenantActionContract — requiere tenantId)"},"contract":{"type":"object","description":"contrato completo: webhookUrl, webhookMethod, contentType, structure (flat|nested|rootArray), authType (None|ApiKey|Bearer|Basic), apiKeyHeaderName, authValue, timeoutSeconds, inputSchema{fields[{fieldPath,sourceType(system|conversation|labelingResult|static),sourceKey,dataType,format,required,staticValue}]}, outputSchema{fields[{fieldPath (soporta wildcard [*]),dataType,mimeType,outputAction(send_to_agent|send_whatsapp_media|inject_context|log_only),label,required}]}, triggerConfig{description,triggerExamples[],requiresConfirmation[]}, chainRules[], requiresAuth, authPolicy, authRequiredMessage"}},"required":["name","description","contract"]}""");
        Add("proponer_contrato_tenant", "Crea un BORRADOR de contrato POR TENANT (TenantActionContract) para una acción global EXISTENTE — la vía correcta para darle a un tenant su propia URL/token sin afectar a otros. Probá el endpoint antes.",
            """{"type":"object","properties":{"actionId":{"type":"string","description":"GUID de la acción global"},"tenantId":{"type":"string"},"contract":{"type":"object","description":"mismo formato que en proponer_accion_webhook"}},"required":["actionId","tenantId","contract"]}""");
        // ── Fase 3: operación ──
        Add("proponer_cron", "Crea un BORRADOR para crear o modificar un JOB programado (cron): nuevo job para una acción, cambiar la expresión cron (HORA PANAMÁ), pausar/reactivar (isActive) o forzar ejecución inmediata (runNow). El NextRunAt se recalcula automáticamente al aplicar.",
            """{"type":"object","properties":{"jobId":{"type":"string","description":"GUID del job existente (omitir para CREAR uno nuevo)"},"actionId":{"type":"string","description":"requerido al crear: GUID de la acción"},"cronExpression":{"type":"string","description":"5 campos estilo Unix en HORA PANAMÁ, ej '*/15 * * * *' o '30 6 * * *'"},"scope":{"type":"string","description":"AllTenants (default) o SingleTenant"},"contextId":{"type":"string","description":"GUID del tenant si scope=SingleTenant"},"isActive":{"type":"boolean"},"runNow":{"type":"boolean","description":"true = ejecutar en el próximo tick del worker (≤60s)"}},"required":[]}""");
        Add("proponer_descarga_ahora", "Crea un BORRADOR para ejecutar UNA descarga inmediata (tenant + acción de descarga) — descarga del endpoint configurado y procesa ítems/grupos/campañas según la config. Útil para validar configs nuevas sin esperar el cron.",
            """{"type":"object","properties":{"actionId":{"type":"string"},"tenantId":{"type":"string"}},"required":["actionId","tenantId"]}""");
        Add("proponer_estado_campana", "Crea un BORRADOR para cambiar el estado de una campaña: pausar (Running→Paused), reanudar (Paused→Running) o LANZAR una Pending (¡esto ENVÍA MENSAJES REALES a los clientes dentro de la ventana de envío y con los rate limits del tenant! — advertilo en tu respuesta).",
            """{"type":"object","properties":{"campaignId":{"type":"string"},"operacion":{"type":"string","description":"pausar | reanudar | lanzar"}},"required":["campaignId","operacion"]}""");
        Add("proponer_config_descarga", "Crea un BORRADOR de configuración de descarga para una acción de descarga (IsDelinquencyDownload): mapeos de campos (roles Phone/ClientName/KeyValue obligatorios) + config del tenant (URL, headers, auto-crear campañas). Es lo que llena el Módulo de Descargas.",
            """{"type":"object","properties":{"actionId":{"type":"string"},"tenantId":{"type":"string"},"mappings":{"type":"array","items":{"type":"object","properties":{"columnKey":{"type":"string"},"jsonPath":{"type":"string","description":"ej $.nombreCliente"},"dataType":{"type":"string","description":"string|number|currency|phone|date"},"displayName":{"type":"string"},"role":{"type":"string","description":"None|Phone|ClientName|KeyValue|Amount|PolicyNumber|ExecutiveEmail|ExecutivePhone"},"roleLabel":{"type":"string","description":"obligatorio si role=KeyValue, ej 'IdPoliza'"},"defaultValue":{"type":"string"}},"required":["columnKey","jsonPath","dataType","displayName"]}},"config":{"type":"object","properties":{"downloadWebhookUrl":{"type":"string"},"downloadWebhookMethod":{"type":"string"},"downloadWebhookHeaders":{"type":"object"},"itemsJsonPath":{"type":"string","description":"null si la respuesta es array raíz"},"autoCrearCampanas":{"type":"boolean"},"campaignTemplateId":{"type":"string"},"autoLaunchCampaigns":{"type":"boolean"},"splitCampaignsByExecutive":{"type":"boolean"}},"required":["downloadWebhookUrl"]}},"required":["actionId","tenantId","mappings","config"]}""");

        return tools;
    }

    private async Task<string> ExecuteToolAsync(string name, JsonObject args, List<DraftDto> drafts, CancellationToken ct)
    {
        string? S(string key) => args[key]?.ToString();
        Guid? G(string key) => Guid.TryParse(S(key), out var g) ? g : null;
        int I(string key, int dflt) => int.TryParse(S(key), out var i) ? Math.Clamp(i, 1, 100) : dflt;

        switch (name)
        {
            case "listar_tenants":
            {
                var rows = await db.Tenants.AsNoTracking()
                    .Select(t => new { t.Id, t.Name, t.IsActive })
                    .OrderBy(t => t.Name).ToListAsync(ct);
                return JsonSerializer.Serialize(rows);
            }
            case "listar_acciones":
            {
                var tenantId = G("tenantId");
                List<string> assigned = [];
                if (tenantId is { } tid)
                {
                    var raw = await db.Tenants.AsNoTracking().Where(t => t.Id == tid)
                        .Select(t => t.AssignedActionIds).FirstOrDefaultAsync(ct);
                    assigned = raw?.Select(g => g.ToString().ToLowerInvariant()).ToList() ?? [];
                }
                var rows = await db.ActionDefinitions.AsNoTracking()
                    .Where(a => a.TenantId == null)
                    .Select(a => new { a.Id, a.Name, a.Description, a.IsActive, a.RequiresWebhook, a.IsDelinquencyDownload })
                    .OrderBy(a => a.Name).ToListAsync(ct);
                var outRows = rows.Select(a => new
                {
                    a.Id, a.Name, a.Description, a.IsActive, a.RequiresWebhook, a.IsDelinquencyDownload,
                    asignadaAlTenant = assigned.Contains(a.Id.ToString().ToLowerInvariant())
                });
                return JsonSerializer.Serialize(outRows);
            }
            case "ver_contrato_accion":
            {
                var actionId = G("actionId") ?? throw new ArgumentException("actionId inválido");
                var tenantId = G("tenantId");
                string? contract = null;
                string source = "global";
                if (tenantId is { } tid)
                {
                    contract = await db.TenantActionContracts.AsNoTracking()
                        .Where(c => c.ActionDefinitionId == actionId && c.TenantId == tid && c.IsActive)
                        .Select(c => c.ContractJson).FirstOrDefaultAsync(ct);
                    if (contract is not null) source = "tenant";
                }
                contract ??= await db.ActionDefinitions.AsNoTracking()
                    .Where(a => a.Id == actionId).Select(a => a.DefaultWebhookContract).FirstOrDefaultAsync(ct);
                if (string.IsNullOrWhiteSpace(contract))
                    return JsonSerializer.Serialize(new { source, contrato = (object?)null, nota = "la acción no tiene contrato configurado" });
                return JsonSerializer.Serialize(new { source, contrato = MaskSecrets(contract) });
            }
            case "listar_prompts":
            {
                var rows = await db.PromptTemplates.AsNoTracking()
                    .Select(p => new { p.Id, p.Name, p.Description, Categoria = db.AgentCategories.Where(c => c.Id == p.CategoryId).Select(c => c.Name).FirstOrDefault(), Largo = p.SystemPrompt!.Length, p.IsActive })
                    .OrderBy(p => p.Name).ToListAsync(ct);
                return JsonSerializer.Serialize(rows);
            }
            case "ver_prompt":
            {
                var id = G("promptId") ?? throw new ArgumentException("promptId inválido");
                var row = await db.PromptTemplates.AsNoTracking().Where(p => p.Id == id)
                    .Select(p => new { p.Id, p.Name, p.Description, p.SystemPrompt }).FirstOrDefaultAsync(ct);
                return JsonSerializer.Serialize(row ?? (object)new { error = "no existe" });
            }
            case "listar_maestros":
            {
                var tid = G("tenantId") ?? throw new ArgumentException("tenantId inválido");
                var rows = await db.CampaignTemplates.AsNoTracking().Where(t => t.TenantId == tid)
                    .Select(t => new
                    {
                        t.Id, t.Name, t.IsActive, t.IsPrimaryForAgent,
                        Agente = db.AgentDefinitions.Where(a => a.Id == t.AgentDefinitionId).Select(a => a.Name).FirstOrDefault(),
                        PromptVinculado = t.PromptTemplateIds,
                        Flujo = db.TenantFlows.Where(f => f.Id == t.ActiveFlowId).Select(f => f.Name).FirstOrDefault(),
                        Acciones = t.ActionIds.Count,
                        Etiquetas = t.LabelIds.Count,
                        HorarioAtencion = t.AttentionStartTime + "-" + t.AttentionEndTime,
                        VentanaEnvio = (t.SendFrom ?? "(sin)") + "-" + (t.SendUntil ?? "(sin)")
                    }).ToListAsync(ct);
                return JsonSerializer.Serialize(rows);
            }
            case "estado_jobs":
            {
                var rows = await db.ScheduledWebhookJobs.AsNoTracking()
                    .Select(j => new
                    {
                        j.Id,
                        Accion = db.ActionDefinitions.Where(a => a.Id == j.ActionDefinitionId).Select(a => a.Name).FirstOrDefault(),
                        j.TriggerType, j.CronExpression, j.TriggerEvent, j.Scope,
                        j.IsActive, j.NextRunAt, j.LastRunAt, j.LastRunStatus, j.LastRunSummary, j.ConsecutiveFailures
                    }).OrderBy(j => j.NextRunAt).ToListAsync(ct);
                return JsonSerializer.Serialize(rows);
            }
            case "historial_descargas":
            {
                var tenantId = G("tenantId");
                var take = I("take", 15);
                var q = db.DelinquencyExecutions.AsNoTracking();
                if (tenantId is { } tid2) q = q.Where(e => e.TenantId == tid2);
                var rows = await q.OrderByDescending(e => e.StartedAt).Take(take)
                    .Select(e => new
                    {
                        e.StartedAt, e.Status,
                        Tenant = db.Tenants.Where(t => t.Id == e.TenantId).Select(t => t.Name).FirstOrDefault(),
                        Accion = db.ActionDefinitions.Where(a => a.Id == e.ActionDefinitionId).Select(a => a.Name).FirstOrDefault(),
                        e.TotalItems, e.ProcessedItems, e.DiscardedItems, e.GroupsCreated, e.CampaignsCreated, e.ErrorMessage
                    }).ToListAsync(ct);
                return JsonSerializer.Serialize(rows);
            }
            case "estado_campanas":
            {
                var tid = G("tenantId") ?? throw new ArgumentException("tenantId inválido");
                var take = I("take", 10);
                var campaigns = await db.Campaigns.AsNoTracking().Where(c => c.TenantId == tid)
                    .OrderByDescending(c => c.CreatedAt).Take(take)
                    .Select(c => new { c.Id, c.Name, Status = c.Status.ToString(), c.TotalContacts, c.CreatedAt, c.LaunchedByUserId })
                    .ToListAsync(ct);
                var ids = campaigns.Select(c => c.Id).ToList();
                var contactAgg = await db.CampaignContacts.AsNoTracking()
                    .Where(cc => ids.Contains(cc.CampaignId))
                    .GroupBy(cc => new { cc.CampaignId, cc.DispatchStatus })
                    .Select(g => new { g.Key.CampaignId, Estado = g.Key.DispatchStatus.ToString(), N = g.Count() })
                    .ToListAsync(ct);
                var rows = campaigns.Select(c => new
                {
                    c.Id, c.Name, c.Status, c.TotalContacts, c.CreatedAt, c.LaunchedByUserId,
                    Contactos = contactAgg.Where(a => a.CampaignId == c.Id).Select(a => new { a.Estado, a.N })
                });
                return JsonSerializer.Serialize(rows);
            }
            case "errores_recientes":
            {
                var horas = I("horas", 24);
                var desde = DateTime.UtcNow.AddHours(-horas);
                var rows = await db.SystemAuditLogs.AsNoTracking()
                    .Where(l => l.OccurredAtUtc > desde)
                    .OrderByDescending(l => l.OccurredAtUtc).Take(30)
                    .Select(l => new { l.OccurredAtUtc, l.Category, l.Severity, Mensaje = l.Message })
                    .ToListAsync(ct);
                return JsonSerializer.Serialize(rows);
            }
            case "proponer_prompt":
            {
                var pname = S("name");
                var sysPrompt = S("systemPrompt");
                if (string.IsNullOrWhiteSpace(pname) || string.IsNullOrWhiteSpace(sysPrompt))
                    throw new ArgumentException("name y systemPrompt son requeridos");
                var payload = new JsonObject
                {
                    ["name"] = pname,
                    ["description"] = S("description"),
                    ["categoryName"] = S("categoryName"),
                    ["systemPrompt"] = sysPrompt,
                    ["maestroId"] = S("maestroId"),
                };
                var exists = await db.PromptTemplates.AsNoTracking().AnyAsync(p => p.Name == pname, ct);
                drafts.Add(new DraftDto("prompt", $"{(exists ? "Actualizar" : "Crear")} prompt: {pname}", payload));
                return JsonSerializer.Serialize(new
                {
                    ok = true,
                    estado = "BORRADOR creado — pendiente de que el usuario haga clic en Aplicar",
                    accion = exists ? "actualizará el prompt existente" : "creará un prompt nuevo",
                });
            }
            case "probar_webhook":
            {
                var url = S("url") ?? throw new ArgumentException("url requerida");
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
                    throw new ArgumentException("solo URLs http/https absolutas");
                // Guard anti-SSRF: no permitir que el copiloto golpee la red interna del server.
                var host = uri.Host.ToLowerInvariant();
                if (host is "localhost" or "127.0.0.1" or "::1" || host.StartsWith("10.") || host.StartsWith("192.168.")
                    || host.StartsWith("169.254.") || System.Text.RegularExpressions.Regex.IsMatch(host, @"^172\.(1[6-9]|2\d|3[01])\."))
                    throw new ArgumentException("host interno/privado no permitido");

                var method = string.Equals(S("method"), "POST", StringComparison.OrdinalIgnoreCase) ? HttpMethod.Post : HttpMethod.Get;
                using var reqMsg = new HttpRequestMessage(method, uri);
                if (args["headers"] is JsonObject hdrs)
                    foreach (var (k, v) in hdrs)
                        reqMsg.Headers.TryAddWithoutValidation(k, v?.ToString());
                var body = S("body");
                if (method == HttpMethod.Post)
                    reqMsg.Content = new StringContent(body ?? "{}", System.Text.Encoding.UTF8, "application/json");

                var client = httpFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(20);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                using var resp = await client.SendAsync(reqMsg, ct);
                var respBody = await resp.Content.ReadAsStringAsync(ct);
                return JsonSerializer.Serialize(new
                {
                    status = (int)resp.StatusCode,
                    ms = sw.ElapsedMilliseconds,
                    contentType = resp.Content.Headers.ContentType?.MediaType,
                    body = Truncate(respBody, 4000),
                });
            }
            case "proponer_accion_webhook":
            {
                var slug = S("name")?.Trim().ToUpperInvariant() ?? throw new ArgumentException("name requerido");
                if (!System.Text.RegularExpressions.Regex.IsMatch(slug, @"^[A-Z][A-Z0-9_]{2,60}$"))
                    throw new ArgumentException("el slug debe ser MAYUSCULAS_CON_GUIONES_BAJOS");
                if (args["contract"] is not JsonObject contractObj || string.IsNullOrWhiteSpace(contractObj["webhookUrl"]?.ToString()))
                    throw new ArgumentException("contract.webhookUrl es requerido");
                var exists = await db.ActionDefinitions.AsNoTracking().AnyAsync(a => a.Name == slug && a.TenantId == null, ct);
                if (exists) throw new ArgumentException($"ya existe una acción global '{slug}' — usá proponer_contrato_tenant para darle contrato a un tenant, o elegí otro slug");
                var payload = new JsonObject
                {
                    ["name"] = slug,
                    ["description"] = S("description"),
                    ["isDelinquencyDownload"] = bool.TryParse(S("isDelinquencyDownload"), out var dd) && dd,
                    ["tenantId"] = S("tenantId"),
                    ["contractScope"] = S("contractScope") ?? "global",
                    ["contract"] = contractObj.DeepClone(),
                };
                drafts.Add(new DraftDto("action", $"Crear acción: {slug}", payload));
                return JsonSerializer.Serialize(new { ok = true, estado = "BORRADOR creado — pendiente de Aplicar por el usuario" });
            }
            case "proponer_contrato_tenant":
            {
                var actionId = G("actionId") ?? throw new ArgumentException("actionId inválido");
                var tenantId = G("tenantId") ?? throw new ArgumentException("tenantId inválido");
                if (args["contract"] is not JsonObject contractObj2 || string.IsNullOrWhiteSpace(contractObj2["webhookUrl"]?.ToString()))
                    throw new ArgumentException("contract.webhookUrl es requerido");
                var accion = await db.ActionDefinitions.AsNoTracking().Where(a => a.Id == actionId).Select(a => a.Name).FirstOrDefaultAsync(ct)
                    ?? throw new ArgumentException("la acción no existe");
                var tenantName = await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.Name).FirstOrDefaultAsync(ct)
                    ?? throw new ArgumentException("el tenant no existe");
                var payload = new JsonObject
                {
                    ["actionId"] = actionId.ToString(),
                    ["tenantId"] = tenantId.ToString(),
                    ["contract"] = contractObj2.DeepClone(),
                };
                drafts.Add(new DraftDto("contract", $"Contrato de {accion} para {tenantName}", payload));
                return JsonSerializer.Serialize(new { ok = true, estado = "BORRADOR creado — pendiente de Aplicar por el usuario" });
            }
            case "proponer_cron":
            {
                var jobId = G("jobId");
                var actionId = G("actionId");
                var cronExpr = S("cronExpression");
                if (jobId is null && actionId is null) throw new ArgumentException("para crear un job indicá actionId; para editar, jobId");
                if (jobId is null && string.IsNullOrWhiteSpace(cronExpr)) throw new ArgumentException("cronExpression requerida al crear");
                if (!string.IsNullOrWhiteSpace(cronExpr))
                {
                    try { Cronos.CronExpression.Parse(cronExpr); }
                    catch { throw new ArgumentException($"expresión cron inválida: {cronExpr}"); }
                }
                string title;
                if (jobId is { } jid)
                {
                    var exists = await db.ScheduledWebhookJobs.AsNoTracking().AnyAsync(j => j.Id == jid, ct);
                    if (!exists) throw new ArgumentException("el job no existe");
                    title = $"Modificar job {jid.ToString()[..8]}";
                }
                else
                {
                    var accion = await db.ActionDefinitions.AsNoTracking().Where(a => a.Id == actionId).Select(a => a.Name).FirstOrDefaultAsync(ct)
                        ?? throw new ArgumentException("la acción no existe");
                    title = $"Crear cron para {accion}";
                }
                var payload = new JsonObject
                {
                    ["jobId"] = jobId?.ToString(),
                    ["actionId"] = actionId?.ToString(),
                    ["cronExpression"] = cronExpr,
                    ["scope"] = S("scope"),
                    ["contextId"] = S("contextId"),
                    ["isActive"] = S("isActive"),
                    ["runNow"] = S("runNow"),
                };
                drafts.Add(new DraftDto("cron", title, payload));
                return JsonSerializer.Serialize(new { ok = true, estado = "BORRADOR creado — pendiente de Aplicar por el usuario" });
            }
            case "proponer_descarga_ahora":
            {
                var actionId = G("actionId") ?? throw new ArgumentException("actionId inválido");
                var tenantId = G("tenantId") ?? throw new ArgumentException("tenantId inválido");
                var cfgOk = await db.ActionDelinquencyConfigs.AsNoTracking()
                    .AnyAsync(c => c.ActionDefinitionId == actionId && c.TenantId == tenantId && c.IsActive, ct);
                if (!cfgOk) throw new ArgumentException("no hay config de descarga activa para ese tenant/acción");
                var accion = await db.ActionDefinitions.AsNoTracking().Where(a => a.Id == actionId).Select(a => a.Name).FirstOrDefaultAsync(ct);
                var tenantName = await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.Name).FirstOrDefaultAsync(ct);
                drafts.Add(new DraftDto("run_download", $"Ejecutar descarga: {accion} ({tenantName})", new JsonObject
                {
                    ["actionId"] = actionId.ToString(),
                    ["tenantId"] = tenantId.ToString(),
                }));
                return JsonSerializer.Serialize(new { ok = true, estado = "BORRADOR creado — pendiente de Aplicar por el usuario" });
            }
            case "proponer_estado_campana":
            {
                var campaignId = G("campaignId") ?? throw new ArgumentException("campaignId inválido");
                var op = S("operacion")?.ToLowerInvariant();
                if (op is not ("pausar" or "reanudar" or "lanzar")) throw new ArgumentException("operacion debe ser pausar | reanudar | lanzar");
                var camp = await db.Campaigns.AsNoTracking().Where(c => c.Id == campaignId)
                    .Select(c => new { c.Name, Status = c.Status.ToString(), c.TotalContacts, c.TenantId }).FirstOrDefaultAsync(ct)
                    ?? throw new ArgumentException("la campaña no existe");
                drafts.Add(new DraftDto("campaign", $"{char.ToUpper(op[0])}{op[1..]} campaña: {Truncate(camp.Name, 50)} ({camp.TotalContacts} contactos, hoy {camp.Status})", new JsonObject
                {
                    ["campaignId"] = campaignId.ToString(),
                    ["operacion"] = op,
                }));
                return JsonSerializer.Serialize(new { ok = true, estado = "BORRADOR creado — pendiente de Aplicar por el usuario", estadoActual = camp.Status });
            }
            case "proponer_config_descarga":
            {
                var actionId = G("actionId") ?? throw new ArgumentException("actionId inválido");
                var tenantId = G("tenantId") ?? throw new ArgumentException("tenantId inválido");
                if (args["mappings"] is not JsonArray maps || maps.Count == 0)
                    throw new ArgumentException("mappings requerido");
                if (args["config"] is not JsonObject cfg || string.IsNullOrWhiteSpace(cfg["downloadWebhookUrl"]?.ToString()))
                    throw new ArgumentException("config.downloadWebhookUrl requerido");
                // Roles obligatorios del pipeline de descargas.
                var roles = maps.OfType<JsonObject>().Select(m => m["role"]?.ToString()).Where(r => r is not null).ToHashSet();
                foreach (var req2 in new[] { "Phone", "ClientName", "KeyValue" })
                    if (!roles.Contains(req2)) throw new ArgumentException($"falta un mapeo con role={req2} (obligatorio)");
                var accion = await db.ActionDefinitions.AsNoTracking().Where(a => a.Id == actionId).Select(a => new { a.Name, a.IsDelinquencyDownload }).FirstOrDefaultAsync(ct)
                    ?? throw new ArgumentException("la acción no existe");
                if (!accion.IsDelinquencyDownload) throw new ArgumentException("la acción no está marcada como descarga (IsDelinquencyDownload)");
                var payload = new JsonObject
                {
                    ["actionId"] = actionId.ToString(),
                    ["tenantId"] = tenantId.ToString(),
                    ["mappings"] = maps.DeepClone(),
                    ["config"] = cfg.DeepClone(),
                };
                drafts.Add(new DraftDto("download_config", $"Config de descarga: {accion.Name}", payload));
                return JsonSerializer.Serialize(new { ok = true, estado = "BORRADOR creado — pendiente de Aplicar por el usuario" });
            }
            default:
                return JsonSerializer.Serialize(new { error = $"herramienta desconocida: {name}" });
        }
    }

    // ─────────────────────────────── Helpers ─────────────────────────────

    private static string BuildSystemPrompt() => """
        Eres el **Copiloto IT** de TalkIA/AgentFlow — el asistente del equipo super-admin de la plataforma
        (agentes IA de cobros/reclamos para corredores de seguros en Panamá). Respondes SIEMPRE en español, claro y directo.

        QUÉ HACES:
        - Diagnosticar procesos con tus herramientas de lectura: crons (estado_jobs), descargas (historial_descargas),
          campañas (estado_campanas), contratos de acciones (ver_contrato_accion), prompts, maestros, errores.
        - Ayudar a crear/mejorar PROMPTS de maestros: redactarlos siguiendo las convenciones y proponerlos con
          `proponer_prompt` (genera un BORRADOR; el usuario lo aplica con un clic — NUNCA digas que ya quedó guardado).
        - CREAR ACCIONES Y WEBHOOKS DESDE CERO (Fase 2), como un wizard conversacional:
          1. Preguntá lo que falte: URL del endpoint, método, autenticación (X-API-Key/Bearer/Basic), forma de la
             respuesta, para qué tenant, cuándo debe dispararla el agente (triggerExamples).
          2. SIEMPRE probá el endpoint con `probar_webhook` ANTES de proponer — verificá status 200 y la forma real
             del JSON (campos, array raíz u objeto). Si falla la auth o la URL, resolvelo con el usuario primero.
          3. Proponé con `proponer_accion_webhook` (acción global nueva + contrato), `proponer_contrato_tenant`
             (darle a un tenant su propia URL/token de una acción existente) o `proponer_config_descarga`
             (mapeos + config del Módulo de Descargas).
          4. El usuario revisa el borrador y hace clic en Aplicar. Después de aplicar una acción conversacional,
             recordale: agregarla al maestro (tab Acciones) para que el agente la vea, y pedir el registro del
             nombre amigable en el catálogo del frontend.

        CONVENCIONES DE CONTRATOS (críticas):
        - inputSchema.fields[].sourceType: `conversation` (el LLM lo pasa por [PARAM:]), `system` (contact.<campo> del
          contacto de campaña, conversation.label.labeledAt, etc.), `labelingResult` (JSON del etiquetado nocturno),
          `static` (staticValue fijo). structure: flat | nested (dot-notation) | rootArray (body = array de objetos,
          para endpoints batch; el valor CSV de un [PARAM:] se expande en elementos).
        - outputSchema.fields[].outputAction: send_to_agent (texto al LLM), send_whatsapp_media (base64→documento al
          cliente; fieldPath soporta wildcard como polizas[*].documentos[*].base64), inject_context, log_only.
        - Acciones CONFIDENCIALES: requiresAuth=true + authRequiredMessage; las que AUTENTICAN llevan authPolicy
          {whenPath, equalsAny, durationMinutes, identityPath}. chainRules: [{when:{path,operator,value}, then:slug|null,
          regenerateReply:true|false, successMessage}].
        - Un dataset de DESCARGA nuevo = una ACCIÓN nueva (nunca reciclar la URL de otra acción de descarga).
          Mapeos de descarga: roles Phone/ClientName/KeyValue OBLIGATORIOS; KeyValue con roleLabel; el KeyValue
          suele ser el idPoliza. Respuesta array raíz → itemsJsonPath null. Descarga vacía = 0 campañas (garantizado).

        CONVENCIONES DE LA PLATAFORMA (apréndelas):
        - Los prompts de maestros NACEN en el catálogo global y se VINCULAN al maestro (que guarda una copia local).
        - El primer mensaje de una campaña lo GENERA el LLM usando el SystemPrompt del maestro + los datos del contacto
          (datos_completos). Un buen prompt define: identidad, estructura del primer mensaje, Q&A con datos, prohibiciones,
          comprobantes → [INTENT:humano], cierre → [INTENT:cierre]. Formato WhatsApp (*negritas*), trato de usted, links en
          su propia línea con dominio en minúsculas, WhatsApp de contacto como https://wa.me/<solo dígitos>.
        - Cron de jobs: expresión en HORA PANAMÁ (UTC-5); NextRunAt en UTC. Campañas: estados Pending (creada, no lanzada),
          Running (enviando dentro de la ventana SendFrom-SendUntil del maestro), Paused, Completed, Closed. Si una campaña
          Running no envía, revisar la ventana de envío y los DispatchStatus de los contactos (Queued/Deferred = esperando).
        - Descargas (morosidad/avisos): una acción global por dataset, config por (tenant, acción), mapeos por acción,
          descarga vacía = 0 campañas. Los KeyValue suelen ser el idPoliza.
        - Las acciones conversacionales se disparan con tags [ACTION:SLUG] desde los prompts; las confidenciales tienen
          requiresAuth y un gate determinista de 2FA.

        REGLAS DURAS:
        - NO tienes ninguna herramienta de SQL ni de estructura de base de datos — no inventes capacidades.
        - Toda escritura es un BORRADOR que el usuario debe Aplicar. Jamás afirmes que algo quedó guardado.
        - No expongas secretos (ya vienen enmascarados); no los pidas.
        - OPERACIÓN (Fase 3), siempre como borrador + Aplicar:
          · `proponer_cron` — crear/modificar jobs programados (cron en HORA PANAMÁ), pausar/reactivar, o runNow
            (ejecución en ≤60s). El NextRunAt se recalcula solo al aplicar.
          · `proponer_descarga_ahora` — ejecutar una descarga inmediata (tenant+acción) para validar configs.
          · `proponer_estado_campana` — pausar/reanudar/LANZAR campañas. ADVERTÍ SIEMPRE al proponer "lanzar":
            envía mensajes REALES a los clientes (dentro de la ventana de envío del maestro y con los rate limits
            anti-ban del tenant). Si una campaña Running "no envía", primero diagnosticá con estado_campanas
            (ventana de envío SendFrom-SendUntil, contactos Deferred) antes de proponer cambios.
        - Si te piden tocar flujos del lienzo o estructura de BD, eso NO está en tu alcance — guialos a la pantalla.
        - Cuando diagnostiques, usa las herramientas ANTES de responder; no especules: cita datos concretos.
        """;

    private static string MaskSecrets(string contractJson)
    {
        try
        {
            var node = JsonNode.Parse(contractJson);
            if (node is JsonObject obj)
            {
                foreach (var key in new[] { "authValue", "apiKey", "password" })
                    if (obj.ContainsKey(key) && obj[key] is not null && !string.IsNullOrEmpty(obj[key]!.ToString()))
                        obj[key] = "•••(oculto)•••";
                if (obj["webhookHeaders"] is not null) obj["webhookHeaders"] = "•••(oculto)•••";
                return obj.ToJsonString();
            }
        }
        catch { /* devolver crudo enmascarando por regex sería peor — mejor nada */ }
        return contractJson;
    }

    private async Task AuditAsync(string category, string message, string relatedId, CancellationToken ct)
    {
        try
        {
            var user = User?.Identity?.Name ?? User?.FindFirst("email")?.Value ?? "super_admin";
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO SystemAuditLog (Id, OccurredAtUtc, TenantId, Category, Severity, Message, RelatedEntityType, RelatedEntityId) " +
                "VALUES (NEWID(), SYSUTCDATETIME(), NULL, {0}, 'Info', {1}, 'Copilot', {2})",
                [category, $"[{user}] {message}", relatedId], ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Copilot] No se pudo auditar");
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
