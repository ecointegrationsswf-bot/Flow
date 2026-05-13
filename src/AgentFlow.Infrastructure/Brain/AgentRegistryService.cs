using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.Infrastructure.Brain;

/// <summary>
/// Implementación de IAgentRegistry.
/// Consulta AgentRegistryEntries por tenant y proyecta a AgentEntry.
/// El AgentDefinitionId se resuelve desde el CampaignTemplate asociado.
/// </summary>
public class AgentRegistryService(AgentFlowDbContext db) : IAgentRegistry
{
    public async Task<List<AgentEntry>> GetAgentsAsync(Guid tenantId, CancellationToken ct = default)
    {
        return await db.AgentRegistryEntries
            .Where(r => r.TenantId == tenantId && r.IsActive)
            .OrderBy(r => r.Name)
            .Select(r => new AgentEntry(
                r.Id,
                r.Slug,
                r.Name,
                r.Capabilities,
                r.CampaignTemplateId,
                r.CampaignTemplate.AgentDefinitionId,
                r.IsWelcome))
            .ToListAsync(ct);
    }

    public async Task<AgentEntry?> GetWelcomeAgentAsync(Guid tenantId, CancellationToken ct = default)
    {
        return await db.AgentRegistryEntries
            .Where(r => r.TenantId == tenantId && r.IsWelcome && r.IsActive)
            .Select(r => new AgentEntry(
                r.Id,
                r.Slug,
                r.Name,
                r.Capabilities,
                r.CampaignTemplateId,
                r.CampaignTemplate.AgentDefinitionId,
                r.IsWelcome))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<AgentEntry?> GetBySlugAsync(Guid tenantId, string slug, CancellationToken ct = default)
    {
        return await db.AgentRegistryEntries
            .Where(r => r.TenantId == tenantId && r.Slug == slug && r.IsActive)
            .Select(r => new AgentEntry(
                r.Id,
                r.Slug,
                r.Name,
                r.Capabilities,
                r.CampaignTemplateId,
                r.CampaignTemplate.AgentDefinitionId,
                r.IsWelcome))
            .FirstOrDefaultAsync(ct);
    }
}
