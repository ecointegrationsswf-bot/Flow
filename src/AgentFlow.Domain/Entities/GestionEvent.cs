using AgentFlow.Domain.Enums;

namespace AgentFlow.Domain.Entities;

/// <summary>
/// Registro de auditoría de cada gestión realizada en una conversación.
/// Soluciona el problema de TalkIA donde se registraban gestiones fantasma.
/// Cada evento tiene origen y timestamp verificable.
/// </summary>
public class GestionEvent
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public Conversation Conversation { get; set; } = null!;

    public GestionResult Result { get; set; }
    public string? Notes { get; set; }
    public string Origin { get; set; } = string.Empty;  // "agent:cobros" | "human:userId"

    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}
