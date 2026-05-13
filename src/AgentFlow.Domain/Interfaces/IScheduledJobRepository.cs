using AgentFlow.Domain.Entities;

namespace AgentFlow.Domain.Interfaces;

/// <summary>
/// Acceso a la tabla ScheduledWebhookJobs. El Worker la consulta cada ciclo
/// para detectar pendientes; los hooks de eventos crean nuevas entradas.
/// </summary>
public interface IScheduledJobRepository
{
    /// <summary>
    /// Devuelve los jobs activos cuya NextRunAt es ≤ now y que no estén Running.
    /// El Worker los toma en cada tick.
    /// </summary>
    Task<List<ScheduledWebhookJob>> GetPendingJobsAsync(DateTime now, CancellationToken ct = default);

    /// <summary>
    /// Devuelve un job por id (para "Ejecutar ahora" y endpoints CRUD).
    /// </summary>
    Task<ScheduledWebhookJob?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Lista para la UI admin. Sin paginación porque el universo de jobs es pequeño.
    /// </summary>
    Task<List<ScheduledWebhookJob>> ListAsync(CancellationToken ct = default);

    /// <summary>
    /// Marca el job como en ejecución para evitar re-pickup en otros ticks
    /// mientras corre. Devuelve true si el cambio se aplicó (control optimista).
    /// </summary>
    Task<bool> MarkRunningAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Persiste el resultado del run: status, summary, próxima ejecución, fallos consecutivos.
    /// </summary>
    Task UpdateAfterRunAsync(
        Guid jobId, string status, string? summary,
        DateTime? nextRunAt, int consecutiveFailures,
        CancellationToken ct = default);

    /// <summary>
    /// Pausa un job (IsActive=false). Lo usa el circuit breaker tras N fallos seguidos.
    /// </summary>
    Task PauseJobAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Libera jobs que quedaron Running por más de cutoff (sin completar). Worker
    /// llama al inicio de cada ciclo para auto-recuperar tras un crash.
    /// </summary>
    Task<int> ResetStuckRunningAsync(DateTime stuckCutoff, CancellationToken ct = default);

    /// <summary>
    /// Crear nuevo job (CRUD admin + EventDispatcher).
    /// </summary>
    Task AddAsync(ScheduledWebhookJob job, CancellationToken ct = default);

    /// <summary>
    /// Actualizar campos editables (CRUD admin). El Worker no debería invocarlo.
    /// </summary>
    Task UpdateAsync(ScheduledWebhookJob job, CancellationToken ct = default);

    /// <summary>
    /// Eliminar un job y su historial (CRUD admin).
    /// </summary>
    Task DeleteAsync(Guid jobId, CancellationToken ct = default);
}
