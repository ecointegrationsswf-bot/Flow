using AgentFlow.Application.Modules.Reports;
using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Enums;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.Reports;

/// <summary>
/// Implementación del cálculo del reporte. Agrupa por <c>ClientPhone</c> para
/// que TODA métrica refleje clientes únicos, no eventos. Multitenant: todas las
/// queries filtran por <c>TenantId</c>.
///
/// Diseño:
///  1. Cargamos las campañas del tenant dentro del rango (filtradas opcionalmente por IDs).
///  2. CampaignContacts: para el universo de teléfonos contactados.
///  3. Conversations + Messages: para detectar respuestas (Inbound) por cliente.
///  4. ConversationLabels: para el MEJOR resultado por cliente (max ranking).
///
/// El "rango de fechas" se aplica sobre <c>Campaign.CreatedAt</c>, que es el
/// momento en que el ejecutivo cargó el archivo — útil para el filtro "Marzo 2026
/// vs Mayo 2026" del gerente. Sub-recursos (contactos, conversaciones, mensajes)
/// se incluyen sin restricción adicional para no perder respuestas que llegaron
/// días después del envío.
/// </summary>
public class EffectivenessReportService(
    AgentFlowDbContext db,
    ILogger<EffectivenessReportService> log) : IEffectivenessReportService
{
    /// <summary>Etiquetas que cuentan como conversión completa.</summary>
    private static readonly string[] PaymentLabels = ["Confimo Pago", "Confirmo Pago"];
    private const string PromiseLabel = "Promesa de Pago";
    private const string NegotiationLabel = "Negociación / Acuerdo";
    private const string DisputeLabel = "Disputa / Reclamo";
    private const string CancelLabel = "Solicita Cancelación";

    public async Task<EffectivenessReportDto> GenerateAsync(
        Guid tenantId,
        DateTime fromUtc,
        DateTime toUtc,
        IReadOnlyList<Guid>? campaignTemplateIds,
        CancellationToken ct)
    {
        // ── 1. Tenant nombre + logo + campañas del rango ────────────────
        // El logo del tenant (URL pública) se inyecta en el header del PDF para
        // que el informe lleve el branding del corredor. Si el tenant no tiene
        // logo configurado, el PDF muestra solo el título — sin placeholder.
        var tenantInfo = await db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.Name, t.LogoUrl })
            .FirstOrDefaultAsync(ct);
        var tenantName = tenantInfo?.Name ?? "(sin nombre)";
        var tenantLogoUrl = tenantInfo?.LogoUrl;

        // Filtrado: el universo se define por las campañas del tenant en el rango,
        // opcionalmente acotado a uno o más MAESTROS (CampaignTemplate). Filtrar
        // por maestro consolida todas las corridas del mismo guion/agente IA —
        // útil para medir desempeño longitudinal de un mismo proceso.
        var campaignsQ = db.Campaigns
            .Where(c => c.TenantId == tenantId
                     && c.CreatedAt >= fromUtc
                     && c.CreatedAt <= toUtc);

        if (campaignTemplateIds is { Count: > 0 })
            campaignsQ = campaignsQ.Where(c =>
                c.CampaignTemplateId != null && campaignTemplateIds.Contains(c.CampaignTemplateId.Value));

        var campaigns = await campaignsQ
            .Select(c => new {
                c.Id, c.Name, c.CreatedAt, c.CampaignTemplateId,
                LaunchedAt = c.LaunchedAt ?? c.CreatedAt
            })
            .ToListAsync(ct);

        var campaignIdList = campaigns.Select(c => c.Id).ToList();

        // Resolvemos el NOMBRE de los maestros para mostrarlo en el header del PDF.
        // Si el usuario filtró por templateIds usamos esa lista; si no filtró,
        // dejamos null (= "todos los maestros del rango").
        IReadOnlyList<string>? templateNamesForFilter = null;
        if (campaignTemplateIds is { Count: > 0 })
        {
            templateNamesForFilter = await db.CampaignTemplates
                .Where(t => t.TenantId == tenantId && campaignTemplateIds.Contains(t.Id))
                .OrderBy(t => t.Name)
                .Select(t => t.Name)
                .ToListAsync(ct);
        }

        log.LogInformation(
            "[EffectivenessReport] Tenant {Tenant} | Rango {From:yyyy-MM-dd}..{To:yyyy-MM-dd} | Campañas={Count}",
            tenantName, fromUtc, toUtc, campaignIdList.Count);

        // ── 2. CampaignContacts del universo (teléfonos contactados) ────
        // Total eventos + únicos + sent + error + distribución por #contactos.
        var contactsByPhone = await db.CampaignContacts
            .Where(cc => campaignIdList.Contains(cc.CampaignId))
            .GroupBy(cc => cc.PhoneNumber)
            .Select(g => new {
                Phone = g.Key,
                Contactos = g.Count(),
                Sent = g.Count(cc => cc.DispatchStatus == DispatchStatus.Sent)
            })
            .ToListAsync(ct);

        var totalContacts = contactsByPhone.Sum(x => x.Contactos);
        var uniqueClients = contactsByPhone.Count;
        var phoneSet = contactsByPhone.Select(x => x.Phone).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // ── 3. Conversaciones del universo (para respuestas y labels) ───
        // Una conversación cuenta si su ClientPhone está en el universo de campaña.
        var conversationsRaw = await (
            from cv in db.Conversations
            where cv.TenantId == tenantId
               && cv.CampaignId != null && campaignIdList.Contains(cv.CampaignId.Value)
            join l in db.ConversationLabels on cv.LabelId equals l.Id into ll
            from l in ll.DefaultIfEmpty()
            select new {
                cv.Id,
                cv.ClientPhone,
                LabelName = l != null ? l.Name : null
            }
        ).ToListAsync(ct);

        var conversationIds = conversationsRaw.Select(c => c.Id).ToList();

        // Inbound messages por conversación (¿el cliente respondió?)
        var conversationsWithInbound = await db.Messages
            .Where(m => conversationIds.Contains(m.ConversationId) && m.Direction == MessageDirection.Inbound)
            .Select(m => m.ConversationId)
            .Distinct()
            .ToListAsync(ct);
        var inboundSet = conversationsWithInbound.ToHashSet();

        // ── 4. Agrupar por cliente único — mejor resultado + respuesta ──
        // RankLabel: ordenamos las etiquetas por valor estratégico.
        //   4=Confirmó Pago, 3=Promesa, 2=Negociación, 1=Disputa, -1=Cancelación,
        //   0=resto/Sin Respuesta. Tomamos el MAX por cliente.
        int RankLabel(string? label) => label switch
        {
            "Confimo Pago" or "Confirmo Pago" => 4,
            PromiseLabel                       => 3,
            NegotiationLabel                   => 2,
            DisputeLabel                       => 1,
            CancelLabel                        => -1,
            _                                  => 0
        };

        var byPhone = conversationsRaw
            .GroupBy(c => c.ClientPhone, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => new {
                    BestRank = g.Max(c => RankLabel(c.LabelName)),
                    Responded = g.Any(c => inboundSet.Contains(c.Id))
                },
                StringComparer.OrdinalIgnoreCase);

        // ── 5. Contar por categoría sobre el universo de CampaignContacts ───
        int respondieron = 0, confirmoPago = 0, promesa = 0, negociacion = 0,
            disputa = 0, cancelacion = 0;

        foreach (var phone in phoneSet)
        {
            if (!byPhone.TryGetValue(phone, out var info)) continue;
            if (info.Responded) respondieron++;
            switch (info.BestRank)
            {
                case  4: confirmoPago++; break;
                case  3: promesa++;      break;
                case  2: negociacion++;  break;
                case  1: disputa++;      break;
                case -1: cancelacion++;  break;
            }
        }

        var sinRespuesta = uniqueClients - (confirmoPago + promesa + negociacion + disputa + cancelacion);

        // ── 6. Distribución del # de contactos por cliente ──────────────
        var distBuckets = contactsByPhone
            .GroupBy(x => x.Contactos >= 4 ? 4 : x.Contactos)   // colapsamos 4+ en un solo bucket
            .OrderBy(g => g.Key)
            .Select(g => new ContactDistributionBucket(
                ContactCount: g.Key,
                Clients: g.Count(),
                PercentageOfUniqueClients: uniqueClients == 0 ? 0m : Math.Round((decimal)g.Count() * 100m / uniqueClients, 2)))
            .ToList();

        // ── 7. Distribución de resultados por cliente único ─────────────
        var resultBuckets = new List<ResultDistributionBucket>
        {
            new("Confirmó Pago",         confirmoPago, Pct(confirmoPago, uniqueClients), "Conversión"),
            new("Promesa de Pago",       promesa,      Pct(promesa, uniqueClients),      "Compromiso"),
            new("Negociación / Acuerdo", negociacion,  Pct(negociacion, uniqueClients),  "Negociación"),
            new("Disputa / Reclamo",     disputa,      Pct(disputa, uniqueClients),      "Disputa"),
            new("Solicita Cancelación",  cancelacion,  Pct(cancelacion, uniqueClients),  "Cancelación"),
            new("Sin Respuesta",         sinRespuesta, Pct(sinRespuesta, uniqueClients), "Sin Respuesta"),
        };

        // ── 8. Breakdown por campaña ────────────────────────────────────
        var perCampaign = new List<CampaignBreakdown>();
        foreach (var c in campaigns.OrderBy(c => c.CreatedAt))
        {
            // Contactos de esta campaña.
            var cContacts = await db.CampaignContacts
                .Where(cc => cc.CampaignId == c.Id)
                .Select(cc => cc.PhoneNumber)
                .ToListAsync(ct);

            var cTotal = cContacts.Count;
            var cUnique = cContacts.Distinct(StringComparer.OrdinalIgnoreCase).Count();

            var cConvs = conversationsRaw
                .Where(cv => cContacts.Any(p => string.Equals(p, cv.ClientPhone, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            var cResponded = cConvs
                .Where(cv => inboundSet.Contains(cv.Id))
                .Select(cv => cv.ClientPhone)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            var cConfirmed = cConvs
                .Where(cv => PaymentLabels.Contains(cv.LabelName))
                .Select(cv => cv.ClientPhone)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            perCampaign.Add(new CampaignBreakdown(
                c.Id, c.Name, c.LaunchedAt, cTotal, cUnique, cResponded, cConfirmed));
        }

        // ── 9. Armar DTO ─────────────────────────────────────────────────
        var avgContactsPerClient = uniqueClients == 0 ? 0m :
            Math.Round((decimal)totalContacts / uniqueClients, 2);

        return new EffectivenessReportDto(
            Filters: new ReportFilters(
                FromUtc: fromUtc,
                ToUtc: toUtc,
                TenantName: tenantName,
                CampaignTemplateNames: templateNamesForFilter,
                GeneratedAtUtc: DateTime.UtcNow,
                TenantLogoUrl: tenantLogoUrl),
            Summary: new ReportSummary(
                TotalCampaigns: campaigns.Count,
                TotalContactsSent: totalContacts,
                UniqueClients: uniqueClients,
                AverageContactsPerClient: avgContactsPerClient,
                ClientsWhoResponded: respondieron,
                EngagementRate: Pct(respondieron, uniqueClients),
                ClientsConfirmedPayment: confirmoPago,
                ConfirmedPaymentRate: Pct(confirmoPago, uniqueClients),
                ClientsWithPromise: promesa,
                ClientsNegotiating: negociacion,
                ClientsWithDispute: disputa,
                ClientsRequestingCancel: cancelacion,
                ClientsSilent: sinRespuesta,
                EffectiveManagementRate: Pct(confirmoPago + promesa + negociacion, uniqueClients)),
            ContactDistribution: new ContactDistribution(distBuckets),
            ResultDistribution: new ResultDistribution(resultBuckets),
            Campaigns: perCampaign);
    }

    private static decimal Pct(int n, int total) =>
        total == 0 ? 0m : Math.Round((decimal)n * 100m / total, 2);
}
