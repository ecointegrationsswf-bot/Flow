namespace AgentFlow.Domain.Entities;

/// <summary>
/// Historial de ejecuciones de un ScheduledWebhookJob. Una fila por cada vez que
/// el Worker invocó el job (incluyendo runs manuales desde el botón "Ejecutar ahora").
/// Permite construir el panel de auditoría: éxito/fallo, contadores y detalle.
/// </summary>
public class ScheduledWebhookJobExecution
{
    public Guid Id { get; set; }

    public Guid JobId { get; set; }
    public ScheduledWebhookJob? Job { get; set; }

    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }

    /// <summary>Pending | Running | Success | PartialFailure | Failed | Skipped.</summary>
    public string Status { get; set; } = "Pending";

    /// <summary>Total de items procesados (1 si es PerConversation, N si es batch).</summary>
    public int TotalRecords { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }

    /// <summary>Mensaje de error o stack si Status != Success. NULL en runs limpios.</summary>
    public string? ErrorDetail { get; set; }

    /// <summary>Worker | Manual | EventDispatcher. Quién creó esta ejecución.</summary>
    public string? TriggeredBy { get; set; }

    /// <summary>
    /// Identificador del contexto procesado en este run.
    /// PerCampaign → CampaignId · PerConversation → ConversationId · AllTenants → null.
    /// </summary>
    public string? ContextId { get; set; }
}
