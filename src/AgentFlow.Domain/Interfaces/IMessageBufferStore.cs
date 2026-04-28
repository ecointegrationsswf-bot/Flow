namespace AgentFlow.Domain.Interfaces;

/// <summary>
/// Buffer de mensajes entrantes con debounce para evitar que el agente responda
/// mensaje por mensaje cuando el cliente escribe por partes. Los mensajes se
/// agrupan en Redis y se procesan como un solo turno cuando el cliente "deja
/// de escribir" por N segundos (configurado en Tenant.MessageBufferSeconds).
///
/// Claves Redis (TTL seguridad 2 min):
///   buf:{tenantId}:{phone}    → LIST JSON de mensajes pendientes.
///   bufts:{tenantId}:{phone}  → ticks UTC del último mensaje (string).
///   buflock:{tenantId}:{phone} → marker de procesamiento en curso.
/// </summary>
public interface IMessageBufferStore
{
    /// <summary>
    /// Agrega un mensaje al buffer y actualiza el timestamp del último mensaje.
    /// </summary>
    Task AppendAsync(Guid tenantId, string phone, BufferedMessage msg, CancellationToken ct = default);

    /// <summary>
    /// Ticks UTC del último AppendAsync. Null si el buffer está vacío/expirado.
    /// </summary>
    Task<long?> GetLastMessageTicksAsync(Guid tenantId, string phone, CancellationToken ct = default);

    /// <summary>
    /// Lee y borra todos los mensajes pendientes en el orden en que se agregaron.
    /// </summary>
    Task<List<BufferedMessage>> DrainAsync(Guid tenantId, string phone, CancellationToken ct = default);

    /// <summary>
    /// Intenta adquirir un lock de procesamiento para esta conversación.
    /// Devuelve true si lo consiguió; false si otro proceso ya lo tiene.
    /// TTL automático para liberarse si el proceso que lo tomó cuelga.
    /// </summary>
    Task<bool> TryAcquireFlushLockAsync(Guid tenantId, string phone, TimeSpan ttl, CancellationToken ct = default);

    Task ReleaseFlushLockAsync(Guid tenantId, string phone, CancellationToken ct = default);
}

/// <summary>
/// Representación de un mensaje pendiente en el buffer.
/// </summary>
public sealed record BufferedMessage(
    string Content,
    string Channel,
    string? ClientName,
    string? ExternalMessageId,
    string? MediaUrl,
    string? MediaType,
    long TimestampTicks);
