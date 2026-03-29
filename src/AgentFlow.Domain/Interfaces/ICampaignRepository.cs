using AgentFlow.Domain.Entities;

namespace AgentFlow.Domain.Interfaces;

/// <summary>
/// Repositorio para gestionar campañas y sus contactos.
/// Define las operaciones de persistencia que el handler necesita
/// sin depender directamente de Entity Framework (arquitectura limpia).
/// </summary>
public interface ICampaignRepository
{
    /// <summary>Crea una campaña con todos sus contactos en una sola transacción.</summary>
    Task<Campaign> CreateWithContactsAsync(Campaign campaign, CancellationToken ct = default);

    /// <summary>Obtiene una campaña por ID, filtrada por tenant.</summary>
    Task<Campaign?> GetByIdAsync(Guid campaignId, Guid tenantId, CancellationToken ct = default);

    /// <summary>Lista campañas de un tenant, ordenadas por fecha de creación descendente.</summary>
    Task<List<Campaign>> ListByTenantAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Actualiza una campaña existente.</summary>
    Task UpdateAsync(Campaign campaign, CancellationToken ct = default);
}
