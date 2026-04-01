namespace AgentFlow.Domain.Entities;

/// <summary>
/// Registro de auditoría de cada intento de envío de mensaje a un contacto.
/// Guarda el snapshot del prompt, el mensaje generado, el payload a UltraMsg
/// y la respuesta, permitiendo trazabilidad completa de qué se envió y por qué.
/// </summary>
public class CampaignDispatchLog
{
    public Guid Id { get; set; }
    public Guid CampaignId { get; set; }
    public Guid CampaignContactId { get; set; }
    public Guid TenantId { get; set; }

    public int AttemptNumber { get; set; }

    // Prompt snapshot — copia exacta del prompt usado en este intento
    public string? PromptSnapshot { get; set; }

    // Datos del contexto del cliente usados para resolver variables
    public string? ContactDataSnapshot { get; set; }

    // Mensaje final generado por IA y enviado al cliente
    public string? GeneratedMessage { get; set; }

    public string PhoneNumber { get; set; } = string.Empty;

    // Respuesta de UltraMsg
    public string? UltraMsgResponse { get; set; }
    public string? ExternalMessageId { get; set; }

    // Resultado
    public string Status { get; set; } = string.Empty;   // Sent | Error | Timeout
    public string? ErrorDetail { get; set; }
    public int DurationMs { get; set; }

    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}
