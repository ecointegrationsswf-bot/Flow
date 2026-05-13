using AgentFlow.Domain.Entities;

namespace AgentFlow.Domain.Interfaces;

/// <summary>
/// Acceso al historial de ejecuciones (ScheduledWebhookJobExecutions). El Worker
/// inserta una fila por cada run, y la UI las consulta para construir el panel
/// de auditoría por job.
/// </summary>
public interface IJobExecutionRepository
{
    /// <summary>
    /// Inserta una ejecución completa (StartedAt + CompletedAt + status final).
    /// Usado al final de un run.
    /// </summary>
    Task InsertAsync(ScheduledWebhookJobExecution execution, CancellationToken ct = default);

    /// <summary>
    /// Inserta una ejecución en estado Pending para reservar la ranura
    /// antes de que el job arranque (útil para "Ejecutar ahora" desde la UI).
    /// </summary>
    Task<Guid> InsertPendingAsync(
        Guid jobId, DateTime startedAt, string triggeredBy, string? contextId,
        CancellationToken ct = default);

    /// <summary>
    /// Marca una ejecución previamente insertada como Pending con su resultado final.
    /// </summary>
    Task UpdateStatusAsync(
        Guid executionId, DateTime completedAt, string status,
        int totalRecords, int successCount, int failureCount, string? errorDetail,
        CancellationToken ct = default);

    /// <summary>
    /// Devuelve el historial reciente de un job, descendente por StartedAt.
    /// </summary>
    Task<List<ScheduledWebhookJobExecution>> GetRecentByJobAsync(
        Guid jobId, int take = 50, CancellationToken ct = default);
}
