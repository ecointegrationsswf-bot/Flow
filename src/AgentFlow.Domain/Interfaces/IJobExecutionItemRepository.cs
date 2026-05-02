using AgentFlow.Domain.Entities;

namespace AgentFlow.Domain.Interfaces;

/// <summary>
/// Acceso al detalle granular por sub-item de una ScheduledWebhookJobExecution.
/// Cada executor que itera sobre tenants/conversaciones/usuarios escribe un
/// item por sub-unidad para que el panel de auditoría muestre cuál falló y por qué.
/// </summary>
public interface IJobExecutionItemRepository
{
    /// <summary>
    /// Inserta uno o varios items asociados a una ejecución. Si la lista está
    /// vacía no hace nada.
    /// </summary>
    Task AddBatchAsync(
        IEnumerable<ScheduledWebhookJobExecutionItem> items,
        CancellationToken ct = default);

    /// <summary>
    /// Devuelve todos los items de una ejecución, ordenados por CreatedAt asc.
    /// </summary>
    Task<List<ScheduledWebhookJobExecutionItem>> GetByExecutionAsync(
        Guid executionId,
        CancellationToken ct = default);
}
