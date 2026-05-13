namespace AgentFlow.Domain.Webhooks;

/// <summary>
/// Construye el SystemContext para un request de ejecución de acción.
/// Lee de BD: Contact, Campaign, Tenant, Conversation.
///
/// Cuando la conversación está cerrada y/o etiquetada (Fase 3), expone
/// automáticamente sourceKeys adicionales:
///   conversation.label.name | .slug | .keywords | .labeledAt
///   conversation.closedAt | .closeReason | .messageCount
///   contact.externalId (del ContactDataJson) | campaign.externalRef
/// </summary>
public interface ISystemContextBuilder
{
    Task<SystemContext> BuildAsync(
        Guid tenantId,
        Guid? campaignId,
        string contactPhone,
        Guid? conversationId,
        string? agentSlug = null,
        CancellationToken ct = default);
}
