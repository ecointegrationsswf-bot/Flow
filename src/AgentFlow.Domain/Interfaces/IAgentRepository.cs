using AgentFlow.Domain.Entities;

namespace AgentFlow.Domain.Interfaces;

/// <summary>
/// Repositorio para acceder a agentes y tenants.
/// Necesario para que el handler de mensajes pueda resolver
/// el agente asignado y la API key del tenant.
/// </summary>
public interface IAgentRepository
{
    Task<AgentDefinition?> GetByIdAsync(Guid agentId, CancellationToken ct = default);
    Task<AgentDefinition?> GetFirstActiveByTenantAsync(Guid tenantId, CancellationToken ct = default);
    Task<Tenant?> GetTenantByIdAsync(Guid tenantId, CancellationToken ct = default);
    Task<CampaignTemplate?> GetCampaignTemplateByIdAsync(Guid templateId, CancellationToken ct = default);
}
