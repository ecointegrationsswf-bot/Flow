using AgentFlow.Domain.Entities;

namespace AgentFlow.Domain.Interfaces;

/// <summary>
/// Catálogo de agentes disponibles por tenant.
/// Provee al ClassifierService las descripciones de capacidad
/// para que Claude tome la decisión de routing.
/// </summary>
public interface IAgentRegistry
{
    Task<List<AgentEntry>> GetAgentsAsync(Guid tenantId, CancellationToken ct = default);
    Task<AgentEntry?> GetWelcomeAgentAsync(Guid tenantId, CancellationToken ct = default);
    Task<AgentEntry?> GetBySlugAsync(Guid tenantId, string slug, CancellationToken ct = default);
}

/// <summary>
/// Vista simplificada de un agente del registro para el clasificador.
/// </summary>
public record AgentEntry(
    Guid Id,
    string Slug,
    string Name,
    string Capabilities,
    Guid CampaignTemplateId,
    Guid AgentDefinitionId,
    bool IsWelcome
);
