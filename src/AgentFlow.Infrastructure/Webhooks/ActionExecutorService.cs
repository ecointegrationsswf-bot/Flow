using AgentFlow.Domain.Enums;
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
    ILogger<ActionExecutorService> logger) : IActionExecutorService
{
    private const string ValidateIdentitySlug = "VALIDATE_IDENTITY";
    private const int CircuitBreakerThreshold = 3;
    private static readonly TimeSpan CircuitBreakerWindow = TimeSpan.FromMinutes(5);

    public async Task<ActionResult> ExecuteAsync(
        string actionSlug,
        Guid tenantId,
        Guid? campaignTemplateId,
        string contactPhone,
        Guid? conversationId,
        CollectedParams collectedParams,
        string? agentSlug = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(actionSlug))
            return ActionResult.NoOp();

        // ── 1. Feature flag por tenant ──
        var tenant = await db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.WebhookContractEnabled })
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

        // ── 3. Cargar ActionDefinition ──
        var actionDef = await db.ActionDefinitions
            .Where(a => a.TenantId == tenantId && a.Name == actionSlug && a.IsActive)
            .FirstOrDefaultAsync(ct);

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

        // ── 4. En Fase 4 solo soportamos SystemOnly ──
        if (actionDef.ParamSource != ParamSource.SystemOnly)
        {
            logger.LogWarning(
                "[ActionExecutor] Action {Action} tiene ParamSource={Source} — no soportado aún (requiere Collecting_Params)",
                actionSlug, actionDef.ParamSource);
            return ActionResult.NoOp();
        }

        // ── 5. Circuit breaker ──
        if (IsCircuitOpen(tenantId, actionSlug))
        {
            logger.LogWarning("[ActionExecutor] Circuit abierto para {Action} tenant={TenantId}",
                actionSlug, tenantId);
            return ActionResult.Fail("Estamos teniendo dificultades. Te contactaremos pronto.");
        }

        // ── 6. Leer configuración del CampaignTemplate.ActionConfigs ──
        if (!campaignTemplateId.HasValue)
        {
            logger.LogWarning("[ActionExecutor] Sin CampaignTemplateId para {Action} — no hay config", actionSlug);
            return ActionResult.NoOp();
        }

        var template = await db.CampaignTemplates
            .Where(t => t.Id == campaignTemplateId.Value && t.TenantId == tenantId)
            .Select(t => new { t.ActionConfigs })
            .FirstOrDefaultAsync(ct);

        var bundle = configReader.Read(template?.ActionConfigs, actionDef.Id);

        if (bundle?.InputSchema is null)
        {
            logger.LogDebug("[ActionExecutor] Sin InputSchema configurado para {Action}, skip", actionSlug);
            return ActionResult.NoOp();
        }

        if (string.IsNullOrEmpty(bundle.EndpointConfig.WebhookUrl))
        {
            logger.LogWarning("[ActionExecutor] WebhookUrl vacío para {Action}", actionSlug);
            return ActionResult.NoOp();
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
        var httpResult = await httpDispatcher.SendAsync(
            bundle.EndpointConfig,
            payload,
            bundle.InputSchema.ContentType ?? "application/json",
            ct);

        if (!httpResult.Success)
        {
            RecordFailure(tenantId, actionSlug);
            logger.LogWarning("[ActionExecutor] HTTP fallo {Action}: {Error}",
                actionSlug, httpResult.ErrorMessage);

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
            return ActionResult.Ok(httpStatus: httpResult.StatusCode);
        }

        var outputCtx = new OutputContext
        {
            TenantId = tenantId,
            ContactPhone = contactPhone,
            ConversationId = conversationId,
            AgentSlug = agentSlug,
            ActionName = actionSlug
        };

        return await outputInterpreter.InterpretAsync(httpResult.Body, bundle.OutputSchema, outputCtx, ct);
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
}
