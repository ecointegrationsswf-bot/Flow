namespace AgentFlow.Domain.Interfaces;

/// <summary>
/// Punto de entrada que el resto del sistema usa para anunciar eventos del ciclo
/// de vida del negocio (CampaignStarted, ConversationClosed, ...). El dispatcher
/// busca jobs ScheduledWebhookJobs cuyo TriggerEvent matchee y crea ejecuciones
/// futuras en BD; el ScheduledWebhookWorker las recoge en su próximo tick.
///
/// Es 100% aditivo respecto al sistema existente — los productores
/// (CampaignDispatcherJob, ProcessIncomingMessageCommand, BrainService) solo
/// agregan una llamada extra; si no hay jobs registrados para ese evento, no
/// pasa nada.
/// </summary>
public interface IWebhookEventDispatcher
{
    /// <summary>
    /// Anuncia un evento. Para EventBased crea ejecución inmediata; para DelayFromEvent
    /// programa NextRunAt = now + DelayMinutes. AllTenants → 1 ejecución global;
    /// PerCampaign / PerConversation → ejecución acotada por contextId.
    /// </summary>
    /// <param name="eventName">CampaignStarted | CampaignFinished | CampaignContactSent | ConversationClosed | ConversationEscalated</param>
    /// <param name="contextId">CampaignId / ConversationId / contactId, etc. Null para AllTenants.</param>
    /// <param name="tenantId">Tenant del evento. Usado por el circuit breaker para aislar fallos por tenant.</param>
    Task DispatchAsync(
        string eventName,
        string? contextId,
        Guid tenantId,
        CancellationToken ct = default);
}
