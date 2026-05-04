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
    // Estados de un contacto en flujo. Sent NO está incluido: significa que ese
    // ciclo terminó para esa campaña y otra campaña posterior puede tomar el
    // teléfono sin marcarlo como duplicado.
    private static readonly List<DispatchStatus> InFlightStatuses = new()
    {
        DispatchStatus.Queued,
        DispatchStatus.Claimed,
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

        // Solo se considera duplicado un teléfono que tiene un mensaje EN FLUJO
        // (Queued/Claimed/Retry) en otra campaña RUNNING + ACTIVA del mismo tenant.
        // Sent ya no marca duplicado: significa que el envío se completó para
        // esa campaña y otra puede dispararle un mensaje nuevo sin conflicto.
        var actives = await (
            from cc in db.CampaignContacts
            join c in db.Campaigns on cc.CampaignId equals c.Id
            where c.TenantId == tenantId
               && cc.CampaignId != excludeCampaignId
               && c.Status == CampaignStatus.Running
               && c.IsActive
               && phoneList.Contains(cc.PhoneNumber)
               && InFlightStatuses.Contains(cc.DispatchStatus)
            select cc.PhoneNumber
        ).Distinct().ToListAsync(ct);

        return actives.ToHashSet();
    }
}
