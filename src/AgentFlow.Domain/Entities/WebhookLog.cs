namespace AgentFlow.Domain.Entities;

/// <summary>
/// Bitácora de cada llamada recibida en el webhook de UltraMsg.
/// Útil para diagnóstico, auditoría y depuración.
/// </summary>
public class WebhookLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Proveedor: "ultramsg" | "meta"</summary>
    public string Provider { get; set; } = "ultramsg";

    /// <summary>InstanceId extraído del payload o query string</summary>
    public string? InstanceId { get; set; }

    /// <summary>TenantId resuelto (null si no se pudo identificar)</summary>
    public Guid? TenantId { get; set; }

    /// <summary>Remitente del mensaje (número de teléfono)</summary>
    public string? FromPhone { get; set; }

    /// <summary>Tipo de mensaje: chat | image | document | ptt | audio</summary>
    public string? MessageType { get; set; }

    /// <summary>Cuerpo del mensaje (texto o URL de media)</summary>
    public string? Body { get; set; }

    /// <summary>ID externo del mensaje en WhatsApp</summary>
    public string? ExternalMessageId { get; set; }

    /// <summary>Resultado del procesamiento</summary>
    public string Status { get; set; } = "received";  // received | processed | ignored | error

    /// <summary>Razón del estado (ej: "duplicate", "unknown instanceId", etc.)</summary>
    public string? StatusReason { get; set; }

    /// <summary>Payload JSON completo recibido</summary>
    public string? RawPayload { get; set; }

    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
}
