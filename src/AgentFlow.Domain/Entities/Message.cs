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
    public string? ExternalMessageId { get; set; }   // ID del mensaje en WhatsApp/UltraMsg

    public bool IsFromAgent { get; set; }
    public string? AgentName { get; set; }           // nombre del agente que respondió

    // Metadata LLM
    public int? TokensUsed { get; set; }
    public double? ConfidenceScore { get; set; }
    public string? DetectedIntent { get; set; }      // cobros / reclamos / renovaciones / humano

    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}
