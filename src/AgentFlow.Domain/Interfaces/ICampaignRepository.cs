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

    /// <summary>
    /// Igual que <see cref="GetByIdAsync"/> pero incluye el Tenant cargado.
    /// Usado por <c>LaunchCampaignV2Handler</c> que necesita BusinessHoursStart/TimeZone
    /// del tenant para calcular el <c>ScheduledFor</c> de los contactos diferidos.
    /// </summary>
    Task<Campaign?> GetByIdWithTenantAsync(Guid campaignId, Guid tenantId, CancellationToken ct = default);

    /// <summary>Lista campañas de un tenant, ordenadas por fecha de creación descendente.</summary>
    Task<List<Campaign>> ListByTenantAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Actualiza una campaña existente.</summary>
    Task UpdateAsync(Campaign campaign, CancellationToken ct = default);

    /// <summary>
    /// Obtiene un contacto de campaña por teléfono. Usado al responder mensajes
    /// entrantes para cargar los datos del Excel (ContactDataJson) y pasarlos al LLM.
    /// </summary>
    Task<CampaignContact?> GetContactByPhoneAsync(Guid campaignId, string phone, CancellationToken ct = default);

    /// <summary>
    /// Marca el ContactGroup de morosidad asociado a este teléfono+tenant como "respondido"
    /// — sólo si CampaignId IS NOT NULL y FirstClientReplyAt IS NULL (idempotente).
    /// Sólo afecta grupos creados con autoCrearCampanas=true.
    /// </summary>
    Task TryMarkContactGroupRepliedAsync(Guid tenantId, string phoneNormalized, DateTime when, CancellationToken ct = default);
}
