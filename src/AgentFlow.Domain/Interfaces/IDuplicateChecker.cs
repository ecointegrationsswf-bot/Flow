namespace AgentFlow.Domain.Interfaces;

/// <summary>
/// Detecta teléfonos que ya tienen una campaña activa para el tenant. Usado por el
/// CampaignIntakeService v2 para no dispararle dos veces el mismo cliente al mismo tiempo
/// desde campañas distintas.
/// </summary>
public interface IDuplicateChecker
{
    /// <summary>
    /// Devuelve el subconjunto de teléfonos que ya están asignados a una campaña
    /// activa del tenant — distinta a <paramref name="excludeCampaignId"/>. Un contacto
    /// está "activo" si su DispatchStatus está en (Queued, Claimed, Sent, Retry).
    /// </summary>
    Task<HashSet<string>> GetActivePhonesAsync(
        Guid tenantId,
        Guid excludeCampaignId,
        IEnumerable<string> phones,
        CancellationToken ct = default);
}
