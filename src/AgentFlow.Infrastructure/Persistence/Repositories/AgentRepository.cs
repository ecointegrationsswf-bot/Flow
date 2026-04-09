using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.Infrastructure.Persistence.Repositories;

public class AgentRepository(AgentFlowDbContext db) : IAgentRepository
{
    public async Task<AgentDefinition?> GetByIdAsync(Guid agentId, CancellationToken ct = default)
        => await db.Set<AgentDefinition>().FirstOrDefaultAsync(a => a.Id == agentId, ct);

    public async Task<AgentDefinition?> GetFirstActiveByTenantAsync(Guid tenantId, CancellationToken ct = default)
        => await db.Set<AgentDefinition>()
            .Where(a => a.TenantId == tenantId && a.IsActive)
            .OrderBy(a => a.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<Tenant?> GetTenantByIdAsync(Guid tenantId, CancellationToken ct = default)
        => await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);

    public async Task<CampaignTemplate?> GetCampaignTemplateByIdAsync(Guid templateId, CancellationToken ct = default)
        => await db.CampaignTemplates.FirstOrDefaultAsync(t => t.Id == templateId, ct);

    public async Task<PromptTemplate?> GetPromptTemplateByIdAsync(Guid promptTemplateId, CancellationToken ct = default)
        => await db.PromptTemplates.FirstOrDefaultAsync(p => p.Id == promptTemplateId, ct);
}
