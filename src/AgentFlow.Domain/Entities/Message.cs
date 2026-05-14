using AgentFlow.Domain.Enums;

namespace AgentFlow.Domain.Entities;

public enum MessageDirection { Inbound, Outbound }
public enum MessageStatus { Sent, Delivered, Read, Failed }

/// <summary>
/// Mensaje individual dentro de una conversación.
/// </summary>
public class Message
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public Conversation Conversation { get; set; } = null!;

    public MessageDirection Direction { get; set; }
    public MessageStatus Status { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? ExternalMessageId { get; set; }   // ID del mensaje en WhatsApp/UltraMsg/Resend

    /// <summary>
    /// Canal por el que se envió este mensaje específico. Si es NULL, hereda
    /// del canal de la conversación (Conversation.Channel). Permite que una
    /// misma conversación tenga mensajes de canales distintos (ej: WhatsApp
    /// + email de cierre + SMS).
    /// </summary>
    public ChannelType? Channel { get; set; }

    /// <summary>
    /// Asunto/título del mensaje. Solo aplica a canales que lo soportan (Email).
    /// Para WhatsApp/SMS queda NULL.
    /// </summary>
    public string? Subject { get; set; }

    /// <summary>Destinatario específico para canales no-WhatsApp (Email/SMS).
    /// Para emails: <c>to=foo@bar.com cc=baz@qux.com</c> serializado simple.</summary>
    public string? Recipient { get; set; }

    public bool IsFromAgent { get; set; }
    public string? AgentName { get; set; }           // nombre del agente que respondió

    // Metadata LLM
    public int? TokensUsed { get; set; }
    public double? ConfidenceScore { get; set; }
    public string? DetectedIntent { get; set; }      // cobros / reclamos / renovaciones / humano

    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}
