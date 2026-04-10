using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Enums;

namespace AgentFlow.Domain.Interfaces;

public interface IConversationRepository
{
    Task<Conversation?> GetActiveByPhoneAsync(Guid tenantId, string phone, CancellationToken ct = default);
    Task<Conversation?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IEnumerable<Conversation>> GetActiveByTenantAsync(Guid tenantId, CancellationToken ct = default);
    Task<Conversation> CreateAsync(Conversation conversation, CancellationToken ct = default);
    Task UpdateAsync(Conversation conversation, CancellationToken ct = default);
    Task AddMessageAsync(Message message, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
    Task<IEnumerable<Conversation>> GetByStatusAsync(Guid tenantId, ConversationStatus status, CancellationToken ct = default);
    Task<Campaign?> GetCampaignAsync(Guid campaignId, CancellationToken ct = default);
    Task<Conversation?> GetLatestByPhoneAsync(Guid tenantId, string phone, CancellationToken ct = default);
}
