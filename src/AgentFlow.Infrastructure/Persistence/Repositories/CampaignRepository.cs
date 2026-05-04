using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementación del repositorio de campañas usando Entity Framework Core.
/// Cada query se filtra por TenantId para garantizar aislamiento multi-tenant.
/// </summary>
public class CampaignRepository(AgentFlowDbContext db) : ICampaignRepository
{
    public async Task<Campaign> CreateWithContactsAsync(Campaign campaign, CancellationToken ct = default)
    {
        // EF Core agrega automáticamente los CampaignContacts
        // porque están en la colección campaign.Contacts (relación 1:N)
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(ct);
        return campaign;
    }

    public async Task<Campaign?> GetByIdAsync(Guid campaignId, Guid tenantId, CancellationToken ct = default)
    {
        return await db.Campaigns
            .Include(c => c.Contacts)
            .FirstOrDefaultAsync(c => c.Id == campaignId && c.TenantId == tenantId, ct);
    }

    public async Task<Campaign?> GetByIdWithTenantAsync(Guid campaignId, Guid tenantId, CancellationToken ct = default)
    {
        return await db.Campaigns
            .Include(c => c.Tenant)
            .Include(c => c.Contacts)
            .FirstOrDefaultAsync(c => c.Id == campaignId && c.TenantId == tenantId, ct);
    }

    public async Task<List<Campaign>> ListByTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        return await db.Campaigns
            .Where(c => c.TenantId == tenantId)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new Campaign
            {
                Id = c.Id,
                TenantId = c.TenantId,
                Name = c.Name,
                AgentDefinitionId = c.AgentDefinitionId,
                Channel = c.Channel,
                Trigger = c.Trigger,
                IsActive = c.IsActive,
                TotalContacts = c.TotalContacts,
                ProcessedContacts = c.ProcessedContacts,
                ScheduledAt = c.ScheduledAt,
                StartedAt = c.StartedAt,
                CompletedAt = c.CompletedAt,
                CreatedAt = c.CreatedAt,
                CreatedByUserId = c.CreatedByUserId,
                SourceFileName = c.SourceFileName,
                Status = c.Status,
                LaunchedAt = c.LaunchedAt,
            })
            .ToListAsync(ct);
    }

    public async Task UpdateAsync(Campaign campaign, CancellationToken ct = default)
    {
        db.Campaigns.Update(campaign);
        await db.SaveChangesAsync(ct);
    }

    public async Task<CampaignContact?> GetContactByPhoneAsync(Guid campaignId, string phone, CancellationToken ct = default)
    {
        return await db.CampaignContacts
            .AsNoTracking()
            .FirstOrDefaultAsync(cc => cc.CampaignId == campaignId && cc.PhoneNumber == phone, ct);
    }

    public async Task TryMarkContactGroupRepliedAsync(
        Guid tenantId, string phoneNormalized, DateTime when, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNormalized)) return;

        // ExecuteUpdateAsync no aplica el HasConversion<string>() del enum, así que actualizamos
        // por change tracker (load + save). Filtro estrecho: tenant + teléfono + autoCrearCampanas
        // (CampaignId NOT NULL) + sin respuesta previa (idempotente).
        var groups = await db.ContactGroups
            .Where(g => g.TenantId == tenantId
                     && g.PhoneNormalized == phoneNormalized
                     && g.CampaignId != null
                     && g.FirstClientReplyAt == null)
            .ToListAsync(ct);

        if (groups.Count == 0) return;

        foreach (var g in groups)
        {
            g.FirstClientReplyAt = when;
            g.Status = ContactGroupStatus.ClientReplied;
        }
        await db.SaveChangesAsync(ct);
    }
}
