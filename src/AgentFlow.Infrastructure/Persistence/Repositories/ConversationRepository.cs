using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.Infrastructure.Persistence.Repositories;

public class ConversationRepository(AgentFlowDbContext db) : IConversationRepository
{
    public async Task<Conversation?> GetActiveByPhoneAsync(Guid tenantId, string phone, CancellationToken ct = default)
        => await db.Conversations
            .Include(c => c.Messages)
            .FirstOrDefaultAsync(c => c.TenantId == tenantId
                && c.ClientPhone == phone
                && c.Status != ConversationStatus.Closed, ct);

    public async Task<Conversation?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await db.Conversations
            .Include(c => c.Messages)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<IEnumerable<Conversation>> GetActiveByTenantAsync(Guid tenantId, CancellationToken ct = default)
        => await db.Conversations
            .Where(c => c.TenantId == tenantId && c.Status != ConversationStatus.Closed)
            .OrderByDescending(c => c.LastActivityAt)
            .ToListAsync(ct);

    public async Task<Conversation> CreateAsync(Conversation conversation, CancellationToken ct = default)
    {
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync(ct);
        return conversation;
    }

    public async Task UpdateAsync(Conversation conversation, CancellationToken ct = default)
    {
        db.Conversations.Update(conversation);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IEnumerable<Conversation>> GetByStatusAsync(Guid tenantId, ConversationStatus status, CancellationToken ct = default)
        => await db.Conversations
            .Where(c => c.TenantId == tenantId && c.Status == status)
            .OrderByDescending(c => c.LastActivityAt)
            .ToListAsync(ct);
}
