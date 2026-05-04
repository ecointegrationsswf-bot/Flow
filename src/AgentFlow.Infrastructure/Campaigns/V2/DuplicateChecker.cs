using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.Infrastructure.Campaigns.V2;

/// <summary>
/// Implementación de <see cref="IDuplicateChecker"/> que consulta CampaignContacts via EF
/// filtrando por TenantId (a través de la navegación Campaign).
///
/// Reemplaza el HTTP a <c>POST /api/campaigns/check-duplicates</c> que hoy hace n8n.
/// </summary>
public class DuplicateChecker(AgentFlowDbContext db) : IDuplicateChecker
{
    // List<T> en lugar de array — el provider InMemory de EF Core 10 preview
    // tropieza con ReadOnlySpan<DispatchStatus> al funccionalizar la query.
    private static readonly List<DispatchStatus> ActiveStatuses = new()
    {
        DispatchStatus.Queued,
        DispatchStatus.Claimed,
        DispatchStatus.Sent,
        DispatchStatus.Retry,
    };

    public async Task<HashSet<string>> GetActivePhonesAsync(
        Guid tenantId,
        Guid excludeCampaignId,
        IEnumerable<string> phones,
        CancellationToken ct = default)
    {
        var phoneList = phones.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToList();
        if (phoneList.Count == 0) return new HashSet<string>();

        // Join explícito en vez de cc.Campaign.TenantId — más portable y
        // funciona con el provider InMemory de EF Core 10 (que no traduce bien
        // navegaciones en algunos casos).
        var actives = await (
            from cc in db.CampaignContacts
            join c in db.Campaigns on cc.CampaignId equals c.Id
            where c.TenantId == tenantId
               && cc.CampaignId != excludeCampaignId
               && phoneList.Contains(cc.PhoneNumber)
               && ActiveStatuses.Contains(cc.DispatchStatus)
            select cc.PhoneNumber
        ).Distinct().ToListAsync(ct);

        return actives.ToHashSet();
    }
}
