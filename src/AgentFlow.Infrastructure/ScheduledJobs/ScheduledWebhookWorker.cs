using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Interfaces;
using Cronos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.ScheduledJobs;

/// <summary>
/// BackgroundService que monitorea la tabla ScheduledWebhookJobs cada 60 segundos.
///
/// Ciclo:
///   1. Reset de jobs atascados en Running > 10 min (auto-recuperación tras crash).
///   2. SELECT de jobs activos con NextRunAt vencido.
///   3. Por cada job: marcar Running (control optimista), invocar al executor que
///      matchee con el slug, persistir resultado en historial y actualizar el job
///      con NextRunAt y contadores de fallos.
///
/// Paralelismo controlado por SemaphoreSlim(10) — protege la BD y a Anthropic
/// de ráfagas. Las Fases 2 y 3 registran executors específicos por slug; en
/// Fase 1 solo existe el DefaultWebhookExecutor con slug "*" como fallback.
///
/// Aislado del Hangfire existente: Hangfire sigue corriendo CampaignDispatcherJob
/// y MessageBufferFlushJob en sus propios workers; este service no compite con ellos.
/// </summary>
public class ScheduledWebhookWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<ScheduledWebhookWorker> log) : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan StuckThreshold = TimeSpan.FromMinutes(10);
    private static readonly int MaxParallelism = 10;
    private const int CircuitBreakerThreshold = 5;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        log.LogInformation("ScheduledWebhookWorker iniciado. Tick={Tick}s.", TickInterval.TotalSeconds);

        // Pequeña pausa al arranque — deja que la app termine de subir antes del primer tick.
        try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                // Cualquier excepción no manejada NO debe matar el worker — log y seguimos.
                log.LogError(ex, "ScheduledWebhookWorker tick falló (continuamos).");
            }

            try { await Task.Delay(TickInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        log.LogInformation("ScheduledWebhookWorker detenido.");
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var jobs = scope.ServiceProvider.GetRequiredService<IScheduledJobRepository>();

        var now = DateTime.UtcNow;
        var resetCount = await jobs.ResetStuckRunningAsync(now - StuckThreshold, ct);
        if (resetCount > 0)
            log.LogWarning("Reset {Count} jobs atascados en Running > {Min}min.", resetCount, StuckThreshold.TotalMinutes);

        var pending = await jobs.GetPendingJobsAsync(now, ct);
        if (pending.Count == 0) return;

        log.LogInformation("Tick: {Count} jobs pendientes.", pending.Count);

        // Paralelismo limitado para no saturar BD/red.
        using var sem = new SemaphoreSlim(MaxParallelism);
        var tasks = pending.Select(async job =>
        {
            await sem.WaitAsync(ct);
            try { await DispatchJobAsync(job, ct); }
            finally { sem.Release(); }
        });
        await Task.WhenAll(tasks);
    }

    private async Task DispatchJobAsync(ScheduledWebhookJob job, CancellationToken ct)
    {
        // Cada job corre en su propio scope para aislar DbContext y servicios scoped.
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;

        var jobs = sp.GetRequiredService<IScheduledJobRepository>();
        var executions = sp.GetRequiredService<IJobExecutionRepository>();
        var executors = sp.GetServices<IScheduledJobExecutor>().ToList();

        // Control optimista: si otro tick ya marcó Running, salimos en silencio.
        if (!await jobs.MarkRunningAsync(job.Id, ct))
        {
            log.LogDebug("Job {Id} ya estaba Running — saltado.", job.Id);
            return;
        }

        var startedAt = DateTime.UtcNow;
        var executionId = await executions.InsertPendingAsync(
            job.Id, startedAt, "Worker", contextId: null, ct);

        JobRunResult result;
        try
        {
            var executor = SelectExecutor(executors, job);
            var ctx = new ScheduledJobContext("Worker", null, startedAt);
            result = await executor.ExecuteAsync(job, ctx, ct);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Job {Id} lanzó excepción no controlada.", job.Id);
            result = JobRunResult.Failed(ex.Message, "Excepción no controlada en el executor.");
        }

        var completedAt = DateTime.UtcNow;
        await executions.UpdateStatusAsync(
            executionId, completedAt, result.Status,
            result.TotalRecords, result.SuccessCount, result.FailureCount,
            result.ErrorDetail, ct);

        var consecutiveFailures = result.Status == "Success" || result.Status == "Skipped"
            ? 0
            : job.ConsecutiveFailures + 1;

        var nextRunAt = ComputeNextRunAt(job, completedAt);
        await jobs.UpdateAfterRunAsync(
            job.Id, result.Status, result.Summary,
            nextRunAt, consecutiveFailures, ct);

        // Circuit breaker: tras N fallos consecutivos pausamos el job. El admin
        // debe reactivarlo manualmente desde la UI tras corregir la causa.
        if (consecutiveFailures >= CircuitBreakerThreshold)
        {
            await jobs.PauseJobAsync(job.Id, ct);
            log.LogWarning(
                "Job {Id} ({Slug}) pausado por circuit breaker tras {Count} fallos seguidos.",
                job.Id, job.ActionDefinition?.Name, consecutiveFailures);
        }
    }

    private static IScheduledJobExecutor SelectExecutor(
        List<IScheduledJobExecutor> executors, ScheduledWebhookJob job)
    {
        var slug = job.ActionDefinition?.Name;
        if (!string.IsNullOrEmpty(slug))
        {
            var match = executors.FirstOrDefault(e =>
                string.Equals(e.Slug, slug, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }
        // Fallback obligatorio: DefaultWebhookExecutor (slug "*").
        return executors.First(e => e.Slug == "*");
    }

    /// <summary>
    /// Calcula la próxima ejecución según TriggerType.
    ///   Cron           → cronos.GetNextOccurrence(now)
    ///   EventBased     → null (re-creado por el dispatcher cuando ocurra el evento)
    ///   DelayFromEvent → null (one-shot; el dispatcher creó la entrada con su delay)
    /// </summary>
    private DateTime? ComputeNextRunAt(ScheduledWebhookJob job, DateTime from)
    {
        if (!string.Equals(job.TriggerType, "Cron", StringComparison.OrdinalIgnoreCase))
            return null;

        if (string.IsNullOrWhiteSpace(job.CronExpression))
            return null;

        try
        {
            var cron = CronExpression.Parse(job.CronExpression);
            return cron.GetNextOccurrence(from, TimeZoneInfo.Utc);
        }
        catch (Exception ex)
        {
            log.LogError(ex,
                "Cron inválido en job {Id}: '{Expr}'. Se desactivará automáticamente.",
                job.Id, job.CronExpression);
            return null;
        }
    }
}
