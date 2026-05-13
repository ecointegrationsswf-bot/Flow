namespace AgentFlow.Domain.Entities;

/// <summary>
/// Bandeja persistente de mensajes entrantes pendientes de procesar.
/// Reemplaza al debouncer in-memory como fuente de verdad: el webhook
/// inserta aquí y el Worker on-prem reclama y procesa. Sobrevive a
/// reciclados de AppPool en hosting compartido (Smartasp).
/// Una fila por ráfaga del mismo (TenantId, FromPhone). Mientras esté
/// en estado Pending las nuevas llegadas concatenan en MessagesJson y
/// resetean LastReceivedAt — equivalente al "reset del timer" del
/// debouncer original, pero durable.
/// </summary>
public class InboundMessageQueueItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }
    public string FromPhone { get; set; } = string.Empty;
    public string Channel { get; set; } = "WhatsApp";
    public Guid? WhatsAppLineId { get; set; }

    public string? ClientName { get; set; }
    public string? ExternalMessageId { get; set; }
    public string? MediaUrl { get; set; }
    public string? MediaType { get; set; }

    /// <summary>JSON array de BufferedMessage — todas las ráfagas del cliente desde FirstReceivedAt.</summary>
    public string MessagesJson { get; set; } = "[]";

    public DateTime FirstReceivedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastReceivedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Segundos de silencio que deben pasar antes de procesar (típico 8-15s).</summary>
    public int BufferSeconds { get; set; } = 12;

    /// <summary>Pending | Claimed | Processing | Replied | Failed | Escalated.</summary>
    public string Status { get; set; } = "Pending";

    public DateTime? ClaimedAt { get; set; }
    public string? ClaimedBy { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
    public string? LastErrorStep { get; set; }

    public Guid? OutboundMessageId { get; set; }
    public Guid? EscalatedToUserId { get; set; }
    public DateTime? EscalatedAt { get; set; }
}

/// <summary>Estados válidos de un item de la cola.</summary>
public static class InboundMessageStatus
{
    public const string Pending    = "Pending";
    public const string Claimed    = "Claimed";
    public const string Processing = "Processing";
    public const string Replied    = "Replied";
    public const string Failed     = "Failed";
    public const string Escalated  = "Escalated";
}
