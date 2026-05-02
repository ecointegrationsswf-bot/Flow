using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.Infrastructure.Persistence.Repositories;

public class JobExecutionItemRepository(AgentFlowDbContext db) : IJobExecutionItemRepository
{
    public async Task AddBatchAsync(
        IEnumerable<ScheduledWebhookJobExecutionItem> items,
        CancellationToken ct = default)
    {
        var list = items.ToList();
        if (list.Count == 0) return;

        var now = DateTime.UtcNow;
        foreach (var it in list)
        {
            if (it.Id == Guid.Empty) it.Id = Guid.NewGuid();
            if (it.CreatedAt == default) it.CreatedAt = now;
        }
        db.ScheduledWebhookJobExecutionItems.AddRange(list);
        await db.SaveChangesAsync(ct);
    }

    public Task<List<ScheduledWebhookJobExecutionItem>> GetByExecutionAsync(
        Guid executionId, CancellationToken ct = default)
        => db.ScheduledWebhookJobExecutionItems
            .AsNoTracking()
            .Where(i => i.ExecutionId == executionId)
            .OrderBy(i => i.CreatedAt)
            .ToListAsync(ct);
}
