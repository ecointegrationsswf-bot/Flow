namespace AgentFlow.Domain.Webhooks;

/// <summary>
/// Construye el SystemContext para un request de ejecución de acción.
/// Lee de BD: Contact, Campaign, Tenant, Conversation.
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
