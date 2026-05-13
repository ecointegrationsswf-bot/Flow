namespace AgentFlow.Domain.Entities;

/// <summary>
/// Auditoría per-call de cada dispatch HTTP que sale del sistema vía
/// IActionExecutorService (incluye webhooks disparados por scheduled jobs,
/// agentes IA con tags [ACTION:xxx], y otros). Una fila por intento — éxito
/// o fallo — con el JSON real enviado y la respuesta del endpoint remoto.
///
/// Permite responder preguntas como:
///  • ¿Se ejecutó el envío para la conversación X?
///  • ¿Qué JSON exacto se mandó al webhook del cliente?
///  • ¿Qué respondió el servidor remoto y en cuánto tiempo?
///  • ¿A qué tenant pertenece cada call?
/// </summary>
public class WebhookDispatchLog
{
    public Guid Id { get; set; }

    // ── Contexto: quién originó el call ──
    /// <summary>Tenant al que corresponde el dispatch. Nullable solo por seguridad — siempre se setea cuando viene de ActionExecutorService.</summary>
    public Guid? TenantId { get; set; }
    public Guid? ConversationId { get; set; }
    public string? ClientPhone { get; set; }

    /// <summary>Si el call fue originado por un ScheduledWebhookJob, ID de la ejecución (FK lógico a ScheduledWebhookJobExecutions).</summary>
    public Guid? JobExecutionId { get; set; }
    public Guid? JobId { get; set; }
    public Guid? ActionDefinitionId { get; set; }
    public string ActionSlug { get; set; } = string.Empty;

    // ── Request ──
    public string TargetUrl { get; set; } = string.Empty;
    public string HttpMethod { get; set; } = "POST";
    public string? RequestContentType { get; set; }
    /// <summary>Payload serializado tal como salió al wire (JSON, query string o form). nvarchar(max).</summary>
    public string? RequestPayloadJson { get; set; }

    // ── Response ──
    public int? ResponseStatusCode { get; set; }
    /// <summary>Body devuelto por el endpoint remoto. nvarchar(max), recortado a 8000 chars en el log.</summary>
    public string? ResponseBody { get; set; }
    public long DurationMs { get; set; }

    /// <summary>Success | Failed.</summary>
    public string Status { get; set; } = "Pending";
    public string? ErrorMessage { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}
