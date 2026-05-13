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
        => await db.CampaignTemplates
            .Include(t => t.Documents)
            .FirstOrDefaultAsync(t => t.Id == templateId, ct);

    public async Task<PromptTemplate?> GetPromptTemplateByIdAsync(Guid promptTemplateId, CancellationToken ct = default)
        => await db.PromptTemplates.FirstOrDefaultAsync(p => p.Id == promptTemplateId, ct);

    public async Task<AgentDefinition?> GetActiveByWhatsAppLineAsync(
        Guid tenantId, Guid whatsAppLineId, CancellationToken ct = default)
        => await db.Set<AgentDefinition>()
            .Where(a => a.TenantId == tenantId
                     && a.IsActive
                     && a.WhatsAppLineId == whatsAppLineId)
            // Defensa: si por alguna razón quedaron varios (no debería pasar
            // por la validación en AgentsController), tomamos el más reciente.
            .OrderByDescending(a => a.UpdatedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<CampaignTemplate?> GetPrimaryTemplateForAgentAsync(
        Guid tenantId, Guid agentDefinitionId, CancellationToken ct = default)
        => await db.CampaignTemplates
            .Include(t => t.Documents)
            .Where(t => t.TenantId == tenantId
                     && t.AgentDefinitionId == agentDefinitionId
                     && t.IsActive
                     && t.IsPrimaryForAgent)
            .OrderByDescending(t => t.UpdatedAt)
            .FirstOrDefaultAsync(ct);
}
