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
            .Include(c => c.Campaign)   // para mostrar el nombre de campaña en el monitor
            .FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<IEnumerable<Conversation>> GetActiveByTenantAsync(Guid tenantId, CancellationToken ct = default)
        => await db.Conversations
            .Include(c => c.ActiveAgent)   // necesario para agentType/agentName en el monitor
            .Include(c => c.Messages)      // necesario para lastMessagePreview
            .Where(c => c.TenantId == tenantId)
            .OrderByDescending(c => c.LastActivityAt)
            .Take(200)                     // límite para no sobrecargar el monitor
            .ToListAsync(ct);

    public async Task<Conversation> CreateAsync(Conversation conversation, CancellationToken ct = default)
    {
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync(ct);
        return conversation;
    }

    public async Task UpdateAsync(Conversation conversation, CancellationToken ct = default)
    {
        var entry = db.Entry(conversation);
        if (entry.State == EntityState.Detached)
            db.Conversations.Update(conversation);

        await db.SaveChangesAsync(ct);
    }

    public async Task AddMessageAsync(Message message, CancellationToken ct = default)
    {
        db.Set<Message>().Add(message);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await db.SaveChangesAsync(ct);
    }

    public async Task<IEnumerable<Conversation>> GetByStatusAsync(Guid tenantId, ConversationStatus status, CancellationToken ct = default)
        => await db.Conversations
            .Where(c => c.TenantId == tenantId && c.Status == status)
            .OrderByDescending(c => c.LastActivityAt)
            .ToListAsync(ct);

    public async Task<Campaign?> GetCampaignAsync(Guid campaignId, CancellationToken ct = default)
        => await db.Campaigns.FirstOrDefaultAsync(c => c.Id == campaignId, ct);

    public async Task<Conversation?> GetLatestByPhoneAsync(Guid tenantId, string phone, CancellationToken ct = default)
        => await db.Conversations
            .Include(c => c.Messages)
            .Where(c => c.TenantId == tenantId && c.ClientPhone == phone)
            .OrderByDescending(c => c.LastActivityAt)
            .FirstOrDefaultAsync(ct);
}
