using AgentFlow.Domain.Entities;

namespace AgentFlow.Domain.Interfaces;

/// <summary>
/// Contrato que implementan los handlers concretos del ScheduledWebhookWorker.
/// Cada executor declara el slug de acción que sabe procesar (matchea con
/// ActionDefinition.Name); el Worker rutea cada job al executor cuyo slug coincida.
///
/// En Fase 1 solo existe un executor genérico (DefaultWebhookExecutor) que invoca
/// IActionExecutorService de forma directa. Fases 2 y 3 registran executors
/// específicos (FollowUp, AutoClose, ConversationLabeling) sin tocar el Worker.
/// </summary>
public interface IScheduledJobExecutor
{
    /// <summary>
    /// Slug que matchea con ActionDefinition.Name. Cada executor concreto declara
    /// el suyo; si dos executors declaran el mismo slug el último registrado gana.
    /// El executor con slug = "*" actúa como fallback genérico.
    /// </summary>
    string Slug { get; }

    /// <summary>
    /// Ejecuta el job. Debe respetar idempotencia (si ya se hizo el trabajo, devolver
    /// Skipped). El Worker envuelve esta llamada con su lock de Running, contadores
    /// de fallos consecutivos y persistencia en historial — el executor solo aporta
    /// la lógica de negocio.
    /// </summary>
    Task<JobRunResult> ExecuteAsync(ScheduledWebhookJob job, ScheduledJobContext ctx, CancellationToken ct);
}

/// <summary>
/// Contexto pasado a un executor durante un run. Permite al executor identificar
/// el alcance (PerCampaign trae CampaignId, PerConversation trae ConversationId)
/// y conocer si el run es manual o automático.
/// </summary>
public sealed record ScheduledJobContext(
    string TriggeredBy,        // Worker | Manual | EventDispatcher
    string? ContextId,         // CampaignId / ConversationId / null para AllTenants
    DateTime RunStartedAt,
    Guid? ExecutionId = null); // FK a ScheduledWebhookJobExecutions, propagado al WebhookDispatchLog
