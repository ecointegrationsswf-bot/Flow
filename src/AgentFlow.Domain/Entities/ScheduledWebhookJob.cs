namespace AgentFlow.Domain.Entities;

/// <summary>
/// Job programable que ejecuta un webhook (vía ActionDefinition) bajo distintos
/// disparadores: Cron, evento del sistema o delay desde un evento. Es la unidad
/// que el ScheduledWebhookWorker monitorea cada ciclo.
///
/// El sistema multi-tenant aplica via Scope:
///   AllTenants     → ejecuta una sola vez para todos los tenants (cron global)
///   PerCampaign    → uno por campaña (ContextId = CampaignId)
///   PerConversation→ uno por conversación (ContextId = ConversationId)
/// </summary>
public class ScheduledWebhookJob
{
    public Guid Id { get; set; }

    /// <summary>Acción a ejecutar — referencia al catálogo de ActionDefinitions.</summary>
    public Guid ActionDefinitionId { get; set; }
    public ActionDefinition? ActionDefinition { get; set; }

    /// <summary>Cron | EventBased | DelayFromEvent. Determina qué campos del job son significativos.</summary>
    public string TriggerType { get; set; } = "Cron";

    /// <summary>Expresión cron de 5 campos (UTC). Solo para TriggerType=Cron.</summary>
    public string? CronExpression { get; set; }

    /// <summary>
    /// Nombre del evento que dispara el job. Solo para EventBased y DelayFromEvent.
    /// Valores soportados:
    ///   CampaignStarted | CampaignFinished | CampaignContactSent
    ///   ConversationClosed | ConversationEscalated
    /// </summary>
    public string? TriggerEvent { get; set; }

    /// <summary>Minutos a esperar tras el evento. Solo para DelayFromEvent.</summary>
    public int? DelayMinutes { get; set; }

    /// <summary>AllTenants | PerCampaign | PerConversation. Define el routing del Worker.</summary>
    public string Scope { get; set; } = "AllTenants";

    public bool IsActive { get; set; } = true;

    /// <summary>UTC. Próxima ejecución programada. NULL hasta que el Worker la calcule.</summary>
    public DateTime? NextRunAt { get; set; }

    /// <summary>UTC. Última ejecución (independiente del estado).</summary>
    public DateTime? LastRunAt { get; set; }

    /// <summary>Pending | Running | Success | PartialFailure | Failed | Skipped.</summary>
    public string? LastRunStatus { get; set; }

    /// <summary>Resumen humano del último run (ej: "100 contactos · 95 OK · 5 fallos").</summary>
    public string? LastRunSummary { get; set; }

    /// <summary>
    /// Contador de fallos consecutivos. Sirve al circuit breaker del WebhookEventDispatcher
    /// para pausar el job tras N fallos seguidos. Se resetea en cada Success.
    /// </summary>
    public int ConsecutiveFailures { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
