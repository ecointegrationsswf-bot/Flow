using System.Text.Json;
using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Provisioning;
using AgentFlow.Domain.Webhooks;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.Webhooks;

/// <summary>
/// Orquestador principal del Webhook Contract System.
///
/// Flujo:
///  1. Feature flag: si Tenant.WebhookContractEnabled = false → NoOp (cero cambios al flujo actual)
///  2. Cargar ActionDefinition por slug + tenant
///  3. Short-circuit: VALIDATE_IDENTITY sigue por ValidationService, no por aquí
///  4. Verificar ParamSource: en esta fase solo SystemOnly se ejecuta
///  5. Circuit breaker: si el endpoint falló 3 veces en 5 min → skip
///  6. Leer InputSchema + OutputSchema del CampaignTemplate.ActionConfigs
///  7. Construir SystemContext + Payload
///  8. Ejecutar HTTP via IHttpDispatcher
///  9. Interpretar respuesta via IOutputInterpreter
/// 10. Devolver ActionResult
/// </summary>
public class ActionExecutorService(
    AgentFlowDbContext db,
    ISystemContextBuilder systemContextBuilder,
    IPayloadBuilder payloadBuilder,
    IHttpDispatcher httpDispatcher,
    IOutputInterpreter outputInterpreter,
    ActionConfigReader configReader,
    IMemoryCache cache,
    ILudoActionEnricher ludoEnricher,
    ILogger<ActionExecutorService> logger) : IActionExecutorService
{
    private const string ValidateIdentitySlug = "VALIDATE_IDENTITY";
    private const int CircuitBreakerThreshold = 3;
    private static readonly TimeSpan CircuitBreakerWindow = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };

    public async Task<ActionResult> ExecuteAsync(
        string actionSlug,
        Guid tenantId,
        Guid? campaignTemplateId,
        string contactPhone,
        Guid? conversationId,
        CollectedParams collectedParams,
        string? agentSlug = null,
        Guid? jobExecutionId = null,
        Guid? jobId = null,
        IReadOnlyDictionary<string, string?>? systemContextOverrides = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(actionSlug))
            return ActionResult.NoOp();

        // ── 1. Feature flag por tenant ──
        var tenant = await db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.WebhookContractEnabled, t.LudoIntegrationEnabled })
            .FirstOrDefaultAsync(ct);

        if (tenant is null || !tenant.WebhookContractEnabled)
        {
            logger.LogDebug("[ActionExecutor] WebhookContractEnabled=false para tenant {TenantId}, skip {Action}",
                tenantId, actionSlug);
            return ActionResult.NoOp();
        }

        // ── 2. Short-circuit: VALIDATE_IDENTITY sigue por ValidationService ──
        if (actionSlug.Equals(ValidateIdentitySlug, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogDebug("[ActionExecutor] VALIDATE_IDENTITY delegado a ValidationService");
            return ActionResult.NoOp();
        }

        // ── 3. Cargar ActionDefinition (incluye DefaultWebhookContract para fallback) ──
        // Aceptamos override por tenant + global (TenantId == null). Preferimos el
        // override del tenant si existe; caemos al global en otro caso.
        var actionCandidates = await db.ActionDefinitions
            .Where(a => (a.TenantId == tenantId || a.TenantId == null)
                        && a.Name == actionSlug
                        && a.IsActive)
            .ToListAsync(ct);

        var actionDef = actionCandidates.FirstOrDefault(a => a.TenantId == tenantId)
                        ?? actionCandidates.FirstOrDefault(a => a.TenantId == null);

        if (actionDef is null)
        {
            logger.LogWarning("[ActionExecutor] ActionDefinition no encontrado: {Action} tenant={TenantId}",
                actionSlug, tenantId);
            return ActionResult.NoOp();
        }

        if (!actionDef.RequiresWebhook)
        {
            logger.LogDebug("[ActionExecutor] Action {Action} no requiere webhook", actionSlug);
            return ActionResult.NoOp();
        }

        // ── 4. Circuit breaker ──
        if (IsCircuitOpen(tenantId, actionSlug))
        {
            logger.LogWarning("[ActionExecutor] Circuit abierto para {Action} tenant={TenantId}",
                actionSlug, tenantId);
            return ActionResult.Fail("Estamos teniendo dificultades. Te contactaremos pronto.");
        }

        // ── 5. Resolver contrato: per-tenant (Acción→Tenant→Contrato) → template → default ──
        ActionConfigBundle? bundle = null;

        // Nivel 0 (arquitectura nueva): contrato per-tenant. Si existe, es autoritativo.
        var tenantContractJson = await TenantActionContractLookup.ResolveContractJsonAsync(db, tenantId, actionSlug, ct);
        if (!string.IsNullOrWhiteSpace(tenantContractJson))
        {
            bundle = BundleFromContractJson(tenantContractJson, actionSlug);
            if (bundle is not null)
                logger.LogDebug("[ActionExecutor] Usando TenantActionContract para {Action} tenant={TenantId}", actionSlug, tenantId);
        }

        // Legacy (solo si el tenant no tiene contrato propio): template → DefaultWebhookContract.
        if (bundle is null)
        {
            // Nivel 1: config del template (override)
            if (campaignTemplateId.HasValue)
            {
                var template = await db.CampaignTemplates
                    .Where(t => t.Id == campaignTemplateId.Value && t.TenantId == tenantId)
                    .Select(t => new { t.ActionConfigs })
                    .FirstOrDefaultAsync(ct);

                bundle = configReader.Read(template?.ActionConfigs, actionDef.Id);
            }

            // Nivel 2: fallback a DefaultWebhookContract de la ActionDefinition
            if (bundle?.InputSchema is null && !string.IsNullOrWhiteSpace(actionDef.DefaultWebhookContract))
            {
                var defaultBundle = BundleFromContractJson(actionDef.DefaultWebhookContract, actionSlug);
                if (defaultBundle is not null)
                {
                    bundle = defaultBundle;
                    logger.LogDebug("[ActionExecutor] Usando DefaultWebhookContract para {Action}", actionSlug);
                }
            }
        }

        if (bundle?.InputSchema is null)
        {
            logger.LogDebug("[ActionExecutor] Sin InputSchema (ni template ni default) para {Action}, skip", actionSlug);
            return ActionResult.NoOp();
        }

        if (string.IsNullOrEmpty(bundle.EndpointConfig.WebhookUrl))
        {
            logger.LogWarning("[ActionExecutor] WebhookUrl vacío para {Action}", actionSlug);
            return ActionResult.NoOp();
        }

        // ── 6b. Integración Ludo (GATED — solo tenants Ludo + slugs Ludo) ──
        // mover_fase necesita dos resoluciones que el LLM no hace: etapa→faseId
        // (StageLabelMap) y teléfono→{oportunidadId} en la URL (GET /prospecto).
        // Los params originales se conservan para el outbox (reintento re-resuelve).
        var isLudoCall = tenant.LudoIntegrationEnabled
                         && LudoIntegrationDefaults.IsLudoActionSlug(actionSlug);
        var originalParams = collectedParams;
        if (isLudoCall)
        {
            var enriched = await ludoEnricher.EnrichAsync(
                tenantId, actionSlug, contactPhone, collectedParams, bundle.EndpointConfig, ct);
            if (!enriched.Success)
                return ActionResult.Fail(enriched.UserMessage ?? "No pude completar la solicitud.");

            bundle = new ActionConfigBundle
            {
                EndpointConfig = enriched.Endpoint,
                InputSchema = bundle.InputSchema,
                OutputSchema = bundle.OutputSchema,
                TriggerConfig = bundle.TriggerConfig,
                ChainRules = bundle.ChainRules,
            };
            collectedParams = enriched.Params;
        }

        // ── 7. Construir SystemContext y Payload ──
        // Obtener CampaignId real (no el template) desde la conversación
        Guid? runtimeCampaignId = null;
        if (conversationId.HasValue)
        {
            runtimeCampaignId = await db.Conversations
                .Where(c => c.Id == conversationId.Value)
                .Select(c => c.CampaignId)
                .FirstOrDefaultAsync(ct);
        }

        var systemCtx = await systemContextBuilder.BuildAsync(
            tenantId, runtimeCampaignId, contactPhone, conversationId, agentSlug, ct);

        // Aplicar overrides al SystemContext después del Build. Útil para handlers
        // que iteran multiples records de un mismo CampaignContact (ej: NOTIFY_GESTION
        // multi-póliza) y necesitan inyectar el KeyValue/policyNumber/etc específico
        // de cada iteración pisando el primer record que el SystemContextBuilder
        // aplana por defecto.
        if (systemContextOverrides is not null)
        {
            foreach (var (key, value) in systemContextOverrides)
                systemCtx.Set(key, value);
        }

        object payload;
        try
        {
            payload = payloadBuilder.Build(bundle.InputSchema, collectedParams, systemCtx);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[ActionExecutor] Error construyendo payload para {Action}", actionSlug);
            return ActionResult.Fail("No pude preparar la solicitud.");
        }

        // ── 8. Ejecutar HTTP ──
        var dispatchStartedAt = DateTime.UtcNow;
        var httpResult = await httpDispatcher.SendAsync(
            bundle.EndpointConfig,
            payload,
            bundle.InputSchema.ContentType ?? "application/json",
            ct);

        // ── Audit log per-call (siempre — éxito o fallo) ──
        await TryWriteDispatchLogAsync(
            tenantId, conversationId, contactPhone,
            jobExecutionId, jobId, actionDef.Id, actionSlug,
            bundle.EndpointConfig.WebhookUrl, bundle.EndpointConfig.WebhookMethod ?? "POST",
            bundle.InputSchema.ContentType ?? "application/json",
            payload, httpResult, dispatchStartedAt, ct);

        if (!httpResult.Success)
        {
            RecordFailure(tenantId, actionSlug);
            logger.LogWarning("[ActionExecutor] HTTP fallo {Action}: {Error}",
                actionSlug, httpResult.ErrorMessage);

            // Degradación suave Ludo: el evento de negocio NO se pierde — queda en el
            // outbox y el LUDO_OUTBOX_DRAINER lo reintenta (mover_fase es idempotente).
            // Solo errores transitorios (5xx/timeout/red); un 4xx es payload/token malo
            // y reintentarlo idéntico no lo arregla.
            // (StatusCode 0 = error de red/timeout — sin respuesta HTTP.)
            if (isLudoCall && httpResult.StatusCode is not (>= 400 and < 500))
                await TryEnqueueLudoOutboxAsync(
                    tenantId, conversationId, contactPhone, actionSlug, originalParams,
                    httpResult.ErrorMessage, ct);

            var msg = httpResult.StatusCode switch
            {
                >= 400 and < 500 => "Hubo un inconveniente procesando tu solicitud.",
                >= 500 => "El servicio está temporalmente no disponible.",
                _ => "En este momento no puedo completar la solicitud. ¿Te contactamos después?"
            };

            return ActionResult.Fail(msg, httpResult.StatusCode);
        }

        // ── 9. Interpretar respuesta ──
        RecordSuccess(tenantId, actionSlug);

        if (bundle.OutputSchema is null || bundle.OutputSchema.Fields.Count == 0)
        {
            logger.LogDebug("[ActionExecutor] Sin OutputSchema, respuesta ignorada");
            // Devolvemos RawResponseJson aunque no haya schema — lo necesita el
            // motor de ChainRule (auto-encadenamiento server-side) para evaluar
            // condiciones sobre la respuesta sin depender del OutputSchema.
            return ActionResult.Ok(httpStatus: httpResult.StatusCode) with { RawResponseJson = httpResult.Body };
        }

        var outputCtx = new OutputContext
        {
            TenantId = tenantId,
            ContactPhone = contactPhone,
            ConversationId = conversationId,
            AgentSlug = agentSlug,
            ActionName = actionSlug
        };

        var interpreted = await outputInterpreter.InterpretAsync(httpResult.Body, bundle.OutputSchema, outputCtx, ct);
        // Adjuntar el body crudo al resultado interpretado para que el orquestador
        // pueda evaluar ChainRules contra el JSON original (independiente del
        // OutputSchema parseado, que puede ocultar campos como `status`).
        return interpreted with { RawResponseJson = httpResult.Body };
    }

    /// <summary>
    /// Integración Ludo — degradación suave. Encola (upsert) el evento fallido en
    /// LudoOutboxItems para que el LUDO_OUTBOX_DRAINER lo reintente. Se guardan los
    /// params ORIGINALES del agente (ej. etapa) — el reintento re-resuelve faseId y
    /// oportunidadId con datos frescos. Nunca lanza: un fallo aquí no rompe el turno.
    /// </summary>
    private async Task TryEnqueueLudoOutboxAsync(
        Guid tenantId, Guid? conversationId, string contactPhone, string actionSlug,
        CollectedParams originalParams, string? error, CancellationToken ct)
    {
        try
        {
            var payloadJson = JsonSerializer.Serialize(originalParams.Values);
            var existing = await db.LudoOutboxItems
                .FirstOrDefaultAsync(o => o.TenantId == tenantId
                                       && o.PhoneE164 == contactPhone
                                       && o.ActionSlug == actionSlug
                                       && o.Status == "Pending", ct);
            if (existing is not null)
            {
                // Ya hay un reintento pendiente para este (tenant, teléfono, acción):
                // refrescamos el payload (el estado más nuevo gana) sin duplicar la cola.
                existing.PayloadJson = payloadJson;
                existing.ConversationId = conversationId ?? existing.ConversationId;
                existing.LastError = Truncate(error, 500);
                existing.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                db.LudoOutboxItems.Add(new LudoOutboxItem
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    ConversationId = conversationId,
                    PhoneE164 = contactPhone,
                    ActionSlug = actionSlug,
                    PayloadJson = payloadJson,
                    Status = "Pending",
                    Attempts = 0,
                    LastError = Truncate(error, 500),
                    NextAttemptAt = DateTime.UtcNow.AddMinutes(2),
                    CreatedAt = DateTime.UtcNow,
                });
            }
            await db.SaveChangesAsync(ct);
            logger.LogInformation("[ActionExecutor] Ludo outbox: encolado {Action} tenant={TenantId} phone={Phone}",
                actionSlug, tenantId, contactPhone);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[ActionExecutor] No pude encolar en LudoOutbox {Action}.", actionSlug);
        }
    }

    private static string? Truncate(string? s, int max) =>
        s is null ? null : (s.Length <= max ? s : s[..max]);

    /// <summary>
    /// Deserializa un ContractJson (shape de DefaultWebhookContract / TenantActionContract)
    /// en un ActionConfigBundle. Devuelve null si el JSON es inválido.
    /// </summary>
    private ActionConfigBundle? BundleFromContractJson(string contractJson, string actionSlug)
    {
        try
        {
            var b = JsonSerializer.Deserialize<ActionConfigBundleJson>(contractJson, _jsonOpts);
            if (b is null) return null;
            return new ActionConfigBundle
            {
                EndpointConfig = new WebhookEndpointConfig
                {
                    WebhookUrl = b.WebhookUrl ?? "",
                    WebhookMethod = b.WebhookMethod ?? "POST",
                    AuthType = b.AuthType ?? "None",
                    AuthValue = b.AuthValue,
                    ApiKeyHeaderName = b.ApiKeyHeaderName ?? "X-Api-Key",
                    WebhookHeaders = b.WebhookHeaders,
                    TimeoutSeconds = b.TimeoutSeconds ?? 10
                },
                InputSchema = b.InputSchema,
                OutputSchema = b.OutputSchema,
                TriggerConfig = b.TriggerConfig,
                ChainRules = b.ChainRules
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning("[ActionExecutor] Contract JSON inválido para {Action}: {Message}",
                actionSlug, ex.Message);
            return null;
        }
    }

    // ── Circuit Breaker ──

    private bool IsCircuitOpen(Guid tenantId, string actionSlug)
    {
        var key = CircuitKey(tenantId, actionSlug);
        return cache.TryGetValue(key, out int failures) && failures >= CircuitBreakerThreshold;
    }

    private void RecordFailure(Guid tenantId, string actionSlug)
    {
        var key = CircuitKey(tenantId, actionSlug);
        cache.TryGetValue(key, out int current);
        cache.Set(key, current + 1, CircuitBreakerWindow);
    }

    private void RecordSuccess(Guid tenantId, string actionSlug)
    {
        cache.Remove(CircuitKey(tenantId, actionSlug));
    }

    private static string CircuitKey(Guid tenantId, string actionSlug) =>
        $"webhook-cb:{tenantId}:{actionSlug}";

    // ── Audit log ──

    private async Task TryWriteDispatchLogAsync(
        Guid tenantId,
        Guid? conversationId,
        string? contactPhone,
        Guid? jobExecutionId,
        Guid? jobId,
        Guid actionDefinitionId,
        string actionSlug,
        string targetUrl,
        string httpMethod,
        string contentType,
        object payload,
        HttpDispatchResult httpResult,
        DateTime startedAt,
        CancellationToken ct)
    {
        try
        {
            string? payloadJson;
            try { payloadJson = JsonSerializer.Serialize(payload, _jsonOpts); }
            catch { payloadJson = payload?.ToString(); }

            var responseBody = httpResult.Body;
            if (!string.IsNullOrEmpty(responseBody) && responseBody.Length > 8000)
                responseBody = responseBody[..8000] + "...[truncated]";

            var log = new WebhookDispatchLog
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ConversationId = conversationId,
                ClientPhone = contactPhone,
                JobExecutionId = jobExecutionId,
                JobId = jobId,
                ActionDefinitionId = actionDefinitionId,
                ActionSlug = actionSlug,
                TargetUrl = targetUrl ?? string.Empty,
                HttpMethod = (httpMethod ?? "POST").ToUpper(),
                RequestContentType = contentType,
                RequestPayloadJson = payloadJson,
                ResponseStatusCode = httpResult.StatusCode == 0 ? null : httpResult.StatusCode,
                ResponseBody = responseBody,
                DurationMs = httpResult.DurationMs,
                Status = httpResult.Success ? "Success" : "Failed",
                ErrorMessage = httpResult.ErrorMessage,
                StartedAt = startedAt,
                CompletedAt = DateTime.UtcNow
            };

            db.WebhookDispatchLogs.Add(log);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Auditoría nunca debe romper el flujo del dispatch.
            logger.LogWarning(ex, "[ActionExecutor] No se pudo persistir WebhookDispatchLog para {Action}.", actionSlug);
        }
    }
}
