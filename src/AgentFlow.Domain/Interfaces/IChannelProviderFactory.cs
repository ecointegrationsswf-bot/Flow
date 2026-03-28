namespace AgentFlow.Domain.Interfaces;

/// <summary>
/// Factory que resuelve el IChannelProvider correcto para un tenant.
/// Busca la línea WhatsApp activa del tenant en BD y construye el provider
/// con el InstanceId y Token de ESA línea — no del appsettings.
///
/// Esto permite que cada tenant tenga su propio número de WhatsApp
/// y sus propias credenciales de UltraMsg.
/// </summary>
public interface IChannelProviderFactory
{
    /// <summary>
    /// Obtiene el provider de canal para un tenant específico.
    /// Busca la primera línea WhatsApp activa del tenant.
    /// </summary>
    Task<IChannelProvider?> GetProviderAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Obtiene el provider usando una línea WhatsApp específica (por ID).
    /// Útil cuando el agente ya sabe qué línea usar (ej: agente vinculado a una línea).
    /// </summary>
    Task<IChannelProvider?> GetProviderByLineAsync(Guid lineId, CancellationToken ct = default);
}
