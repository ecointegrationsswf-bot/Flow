using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Interfaces;

namespace CampaignIntakeTests.Stubs;

/// <summary>
/// IWebhookEventDispatcher no-op para tests. Registra los eventos disparados
/// para poder verificar el wiring del dispatcher.
/// </summary>
public class StubWebhookEventDispatcher : IWebhookEventDispatcher
{
    public List<(string Event, string? Context, Guid Tenant)> Events { get; } = new();

    public Task DispatchAsync(string eventName, string? contextId, Guid tenantId, CancellationToken ct = default)
    {
        Events.Add((eventName, contextId, tenantId));
        return Task.CompletedTask;
    }
}

/// <summary>
/// IScheduledJobRepository no-op. Las llamadas que el dispatcher hace a este repo
/// (programar follow-ups y auto-close) deben ser silenciosas en los tests.
/// </summary>
public class StubScheduledJobRepository : IScheduledJobRepository
{
    public List<ScheduledWebhookJob> Added { get; } = new();

    public Task AddAsync(ScheduledWebhookJob job, CancellationToken ct = default)
    {
        Added.Add(job);
        return Task.CompletedTask;
    }

    public Task<List<ScheduledWebhookJob>> GetPendingJobsAsync(DateTime now, CancellationToken ct = default)
        => Task.FromResult(new List<ScheduledWebhookJob>());

    public Task<ScheduledWebhookJob?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult<ScheduledWebhookJob?>(null);

    public Task<List<ScheduledWebhookJob>> ListAsync(CancellationToken ct = default)
        => Task.FromResult(new List<ScheduledWebhookJob>());

    public Task<bool> MarkRunningAsync(Guid jobId, CancellationToken ct = default)
        => Task.FromResult(true);

    public Task UpdateAfterRunAsync(Guid jobId, string status, string? summary,
        DateTime? nextRunAt, int consecutiveFailures, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task PauseJobAsync(Guid jobId, CancellationToken ct = default) => Task.CompletedTask;

    public Task<int> ResetStuckRunningAsync(DateTime stuckCutoff, CancellationToken ct = default)
        => Task.FromResult(0);

    public Task UpdateAsync(ScheduledWebhookJob job, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task DeleteAsync(Guid jobId, CancellationToken ct = default) => Task.CompletedTask;
}
