using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementación EF Core del IScheduledJobRepository. Todas las queries respetan
/// índices definidos en la configuration (IsActive+NextRunAt, TriggerEvent).
/// </summary>
public class ScheduledJobRepository(AgentFlowDbContext db) : IScheduledJobRepository
{
    public Task<List<ScheduledWebhookJob>> GetPendingJobsAsync(DateTime now, CancellationToken ct = default)
        => db.ScheduledWebhookJobs
            .AsNoTracking()
            .Include(j => j.ActionDefinition)
            .Where(j => j.IsActive
                        && j.NextRunAt != null
                        && j.NextRunAt <= now
                        && (j.LastRunStatus == null || j.LastRunStatus != "Running"))
            .OrderBy(j => j.NextRunAt)
            .ToListAsync(ct);

    public Task<ScheduledWebhookJob?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => db.ScheduledWebhookJobs
            .Include(j => j.ActionDefinition)
            .FirstOrDefaultAsync(j => j.Id == id, ct);

    public Task<List<ScheduledWebhookJob>> ListAsync(CancellationToken ct = default)
        => db.ScheduledWebhookJobs
            .AsNoTracking()
            .Include(j => j.ActionDefinition)
            .OrderByDescending(j => j.UpdatedAt)
            .ToListAsync(ct);

    public async Task<bool> MarkRunningAsync(Guid jobId, CancellationToken ct = default)
    {
        // ExecuteUpdate produce un UPDATE sin tracking; retorna #filas afectadas.
        // Si otro tick ya marcó el job, el WHERE no matchea y devolvemos false.
        var affected = await db.ScheduledWebhookJobs
            .Where(j => j.Id == jobId
                        && (j.LastRunStatus == null || j.LastRunStatus != "Running"))
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.LastRunStatus, "Running")
                .SetProperty(j => j.LastRunAt, DateTime.UtcNow)
                .SetProperty(j => j.UpdatedAt, DateTime.UtcNow), ct);
        return affected > 0;
    }

    public async Task UpdateAfterRunAsync(
        Guid jobId, string status, string? summary,
        DateTime? nextRunAt, int consecutiveFailures,
        CancellationToken ct = default)
    {
        await db.ScheduledWebhookJobs
            .Where(j => j.Id == jobId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.LastRunStatus, status)
                .SetProperty(j => j.LastRunSummary, summary)
                .SetProperty(j => j.NextRunAt, nextRunAt)
                .SetProperty(j => j.ConsecutiveFailures, consecutiveFailures)
                .SetProperty(j => j.UpdatedAt, DateTime.UtcNow), ct);
    }

    public async Task PauseJobAsync(Guid jobId, CancellationToken ct = default)
    {
        await db.ScheduledWebhookJobs
            .Where(j => j.Id == jobId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.IsActive, false)
                .SetProperty(j => j.UpdatedAt, DateTime.UtcNow), ct);
    }

    public async Task<int> ResetStuckRunningAsync(DateTime stuckCutoff, CancellationToken ct = default)
    {
        return await db.ScheduledWebhookJobs
            .Where(j => j.LastRunStatus == "Running"
                        && j.LastRunAt != null
                        && j.LastRunAt < stuckCutoff)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.LastRunStatus, "Failed")
                .SetProperty(j => j.LastRunSummary, "Auto-reset: job atascado en Running > umbral.")
                .SetProperty(j => j.UpdatedAt, DateTime.UtcNow), ct);
    }

    public async Task AddAsync(ScheduledWebhookJob job, CancellationToken ct = default)
    {
        job.CreatedAt = DateTime.UtcNow;
        job.UpdatedAt = DateTime.UtcNow;
        db.ScheduledWebhookJobs.Add(job);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(ScheduledWebhookJob job, CancellationToken ct = default)
    {
        job.UpdatedAt = DateTime.UtcNow;
        db.ScheduledWebhookJobs.Update(job);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid jobId, CancellationToken ct = default)
    {
        // Las ejecuciones se borran por cascada (FK con DeleteBehavior.Cascade).
        await db.ScheduledWebhookJobs
            .Where(j => j.Id == jobId)
            .ExecuteDeleteAsync(ct);
    }
}
