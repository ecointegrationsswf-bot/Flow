using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.Infrastructure.Persistence.Repositories;

public class JobExecutionRepository(AgentFlowDbContext db) : IJobExecutionRepository
{
    public async Task InsertAsync(ScheduledWebhookJobExecution execution, CancellationToken ct = default)
    {
        if (execution.Id == Guid.Empty) execution.Id = Guid.NewGuid();
        db.ScheduledWebhookJobExecutions.Add(execution);
        await db.SaveChangesAsync(ct);
    }

    public async Task<Guid> InsertPendingAsync(
        Guid jobId, DateTime startedAt, string triggeredBy, string? contextId,
        CancellationToken ct = default)
    {
        var exec = new ScheduledWebhookJobExecution
        {
            Id = Guid.NewGuid(),
            JobId = jobId,
            StartedAt = startedAt,
            CompletedAt = startedAt, // se sobrescribe en UpdateStatusAsync
            Status = "Pending",
            TriggeredBy = triggeredBy,
            ContextId = contextId
        };
        db.ScheduledWebhookJobExecutions.Add(exec);
        await db.SaveChangesAsync(ct);
        return exec.Id;
    }

    public async Task UpdateStatusAsync(
        Guid executionId, DateTime completedAt, string status,
        int totalRecords, int successCount, int failureCount, string? errorDetail,
        CancellationToken ct = default)
    {
        await db.ScheduledWebhookJobExecutions
            .Where(e => e.Id == executionId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.CompletedAt, completedAt)
                .SetProperty(e => e.Status, status)
                .SetProperty(e => e.TotalRecords, totalRecords)
                .SetProperty(e => e.SuccessCount, successCount)
                .SetProperty(e => e.FailureCount, failureCount)
                .SetProperty(e => e.ErrorDetail, errorDetail), ct);
    }

    public Task<List<ScheduledWebhookJobExecution>> GetRecentByJobAsync(
        Guid jobId, int take = 50, CancellationToken ct = default)
        => db.ScheduledWebhookJobExecutions
            .AsNoTracking()
            .Where(e => e.JobId == jobId)
            .OrderByDescending(e => e.StartedAt)
            .Take(take)
            .ToListAsync(ct);
}
