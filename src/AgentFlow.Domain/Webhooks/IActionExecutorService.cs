namespace AgentFlow.Domain.Webhooks;

/// <summary>
/// Orquestador del Webhook Contract System.
///
/// Dado un actionSlug detectado por el agente (via tag [ACTION:xxx]),
/// carga la configuración del tenant (InputSchema + OutputSchema desde
/// CampaignTemplates.ActionConfigs), construye el payload, ejecuta el HTTP call
/// e interpreta la respuesta según el schema declarado.
///
/// Cero lógica hardcodeada por tipo de acción — todo viene de los schemas.
/// </summary>
public interface IActionExecutorService
{
    Task<ActionResult> ExecuteAsync(
        string actionSlug,
        Guid tenantId,
        Guid? campaignTemplateId,
        string contactPhone,
        Guid? conversationId,
        CollectedParams collectedParams,
        string? agentSlug = null,
        CancellationToken ct = default);
}
