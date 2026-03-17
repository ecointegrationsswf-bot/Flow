using AgentFlow.Domain.Enums;

namespace AgentFlow.Domain.Entities;

/// <summary>
/// Conversación completa entre el agente y un cliente.
/// Una conversación puede cambiar de agente activo (handoff) durante su ciclo de vida.
/// </summary>
public class Conversation
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;

    // Contacto
    public string ClientPhone { get; set; } = string.Empty;
    public string? ClientName { get; set; }
    public string? PolicyNumber { get; set; }
    public ChannelType Channel { get; set; }

    // Agente activo
    public Guid? ActiveAgentId { get; set; }
    public AgentDefinition? ActiveAgent { get; set; }
    public Guid? CampaignId { get; set; }
    public Campaign? Campaign { get; set; }

    // Estado
    public ConversationStatus Status { get; set; } = ConversationStatus.Active;
    public bool IsHumanHandled { get; set; }
    public string? HandledByUserId { get; set; }

    // Resultado de gestión
    public GestionResult GestionResult { get; set; } = GestionResult.Pending;
    public string? ClosingNote { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAt { get; set; }
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;

    public ICollection<Message> Messages { get; set; } = [];
    public ICollection<GestionEvent> GestionEvents { get; set; } = [];
}
