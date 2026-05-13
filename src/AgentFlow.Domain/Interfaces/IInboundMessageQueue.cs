using AgentFlow.Domain.Entities;

namespace AgentFlow.Domain.Interfaces;

/// <summary>
/// Cola durable de mensajes entrantes. Reemplaza al debouncer in-memory
/// como fuente de verdad. Operaciones idempotentes y seguras para
/// concurrencia entre el API (que solo upserta) y el Worker on-prem
/// (que reclama y completa).
/// </summary>
public interface IInboundMessageQueue
{
    /// <summary>
    /// Encola un mensaje. Si existe una fila Pending para el mismo
    /// (TenantId, FromPhone), concatena en MessagesJson y resetea
    /// LastReceivedAt — equivalente al reset del timer del debouncer.
    /// Idempotente por ExternalMessageId: si llega el mismo webhook
    /// dos veces, no duplica.
    /// </summary>
    Task<Guid> UpsertAsync(InboundMessageUpsertRequest request, CancellationToken ct);

    /// <summary>
    /// Marca como Replied. Llamado por el debouncer/dispatcher al
    /// completar el procesamiento exitosamente.
    /// </summary>
    Task MarkRepliedAsync(Guid id, Guid? outboundMessageId, CancellationToken ct);

    /// <summary>
    /// Marca como Failed con paso y error. Si AttemptCount + 1 &lt; maxAttempts
    /// la fila vuelve a Pending para que la reclamen de nuevo. Si supera el
    /// límite queda en Failed esperando al watchdog.
    /// </summary>
    Task MarkFailedAsync(Guid id, string step, string error, int maxAttempts, CancellationToken ct);

    /// <summary>
    /// Reclama hasta batchSize filas listas para procesar (Pending con
    /// LastReceivedAt + BufferSeconds vencido, o Claimed huérfanas > 5 min).
    /// Marca como Claimed atómicamente. Implementación SQL usa
    /// READPAST + UPDLOCK para que dos workers no peleen por la misma fila.
    /// </summary>
    Task<IReadOnlyList<InboundMessageQueueItem>> ClaimReadyAsync(
        string workerId, int batchSize, CancellationToken ct);

    /// <summary>
    /// Mensajes que llevan más de thresholdSeconds sin alcanzar Replied/Escalated.
    /// Usado por el watchdog para enviar canned y notificar a humanos.
    /// </summary>
    Task<IReadOnlyList<InboundMessageQueueItem>> FindStuckAsync(
        int thresholdSeconds, CancellationToken ct);

    /// <summary>
    /// Marca como Escalated. El watchdog la usa después de enviar el canned
    /// y emitir el SignalR al ejecutivo del tenant.
    /// </summary>
    Task MarkEscalatedAsync(Guid id, Guid? userId, CancellationToken ct);
}

/// <summary>Petición de upsert al webhook receiver.</summary>
public record InboundMessageUpsertRequest(
    Guid TenantId,
    string FromPhone,
    string Channel,
    Guid? WhatsAppLineId,
    string MessageContent,
    string? ClientName,
    string? ExternalMessageId,
    string? MediaUrl,
    string? MediaType,
    int BufferSeconds);
