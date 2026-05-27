using AgentFlow.Domain.Entities;
using AgentFlow.Infrastructure.Persistence;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.Reports;

/// <summary>
/// Genera el Excel "Detalle de Conversaciones" — mismo formato que el resumen
/// que el job nocturno SEND_LABELING_SUMMARY envía por email cada mañana, pero
/// ejecutado a demanda con filtros del usuario (rango de fecha, maestro de
/// campaña opcional, toggle para incluir conversaciones inbound sin campaña).
///
/// Columnas del Excel (alineadas con SendLabelingSummaryExecutor):
///   Campaña · Cliente · Celular · Identificación · Fecha Gestión · Etiqueta
///   · Resumen de la conversación · Usuario · Apellido · Agente
///
/// Las filas de "conversaciones inbound sin campaña" (cuando el toggle está ON)
/// usan "(sin campaña)" en la columna Campaña y se filtran por la fecha del
/// PRIMER mensaje Inbound del cliente — para representar "clientes que nos
/// escribieron espontáneamente en el período".
/// </summary>
public class ConversationDetailsExcelExporter(
    AgentFlowDbContext db,
    ILogger<ConversationDetailsExcelExporter> log)
{
    public async Task<byte[]> GenerateAsync(
        Guid tenantId,
        DateTime fromUtc,
        DateTime toUtc,
        Guid? campaignTemplateId,
        bool includeInboundWithoutCampaign,
        CancellationToken ct)
    {
        // ── 1. Campañas del rango (filtro opcional por maestro) ───────────
        var campaignsQ = db.Campaigns
            .Where(c => c.TenantId == tenantId
                     && c.CreatedAt >= fromUtc
                     && c.CreatedAt <= toUtc);
        if (campaignTemplateId.HasValue)
            campaignsQ = campaignsQ.Where(c => c.CampaignTemplateId == campaignTemplateId.Value);

        var campaigns = await campaignsQ
            .Select(c => new { c.Id, c.Name, c.LaunchedByUserId })
            .ToListAsync(ct);
        var campaignIds = campaigns.Select(c => c.Id).ToList();

        // ── 2. Filas desde conversaciones de esas campañas ────────────────
        var rowsFromCampaigns = await (
            from conv in db.Conversations
            join camp in db.Campaigns on conv.CampaignId equals camp.Id
            join cc in db.CampaignContacts on new { CampId = conv.CampaignId!.Value, conv.ClientPhone }
                                         equals new { CampId = cc.CampaignId, ClientPhone = cc.PhoneNumber } into ccLeft
            from cc in ccLeft.DefaultIfEmpty()
            join lbl in db.Set<ConversationLabel>() on conv.LabelId equals lbl.Id into lblLeft
            from lbl in lblLeft.DefaultIfEmpty()
            join agent in db.AgentDefinitions on conv.ActiveAgentId equals agent.Id into agentLeft
            from agent in agentLeft.DefaultIfEmpty()
            where conv.TenantId == tenantId
               && conv.CampaignId != null && campaignIds.Contains(conv.CampaignId.Value)
            select new ConvRow
            {
                CampaignName = camp.Name,
                ClientName = cc != null ? cc.ClientName : conv.ClientName,
                ClientPhone = conv.ClientPhone,
                Identification = cc != null ? cc.PolicyNumber : null,
                LastActivity = conv.LastActivityAt,
                LabelName = lbl != null ? lbl.Name : null,
                LabeledAt = conv.LabeledAt,
                ConversationId = conv.Id,
                CampaignLaunchedByUserId = camp.LaunchedByUserId,
                AgentName = agent != null ? agent.Name : null,
            }).ToListAsync(ct);

        // ── 3. Filas opcionales — conversaciones SIN campaña con inbound en el período ─
        var rowsFromOrphans = new List<ConvRow>();
        if (includeInboundWithoutCampaign)
        {
            // Una conversación cuenta acá si: (a) no tiene CampaignId, (b) tiene al
            // menos un mensaje Inbound (el cliente escribió) con SentAt en el rango.
            rowsFromOrphans = await (
                from conv in db.Conversations
                join lbl in db.Set<ConversationLabel>() on conv.LabelId equals lbl.Id into lblLeft
                from lbl in lblLeft.DefaultIfEmpty()
                join agent in db.AgentDefinitions on conv.ActiveAgentId equals agent.Id into agentLeft
                from agent in agentLeft.DefaultIfEmpty()
                where conv.TenantId == tenantId
                   && conv.CampaignId == null
                   && db.Messages.Any(m => m.ConversationId == conv.Id
                                        && m.Direction == MessageDirection.Inbound
                                        && m.SentAt >= fromUtc && m.SentAt < toUtc)
                select new ConvRow
                {
                    CampaignName = "(sin campaña)",
                    ClientName = conv.ClientName,
                    ClientPhone = conv.ClientPhone,
                    Identification = null,
                    LastActivity = conv.LastActivityAt,
                    LabelName = lbl != null ? lbl.Name : null,
                    LabeledAt = conv.LabeledAt,
                    ConversationId = conv.Id,
                    CampaignLaunchedByUserId = null,
                    AgentName = agent != null ? agent.Name : null,
                }).ToListAsync(ct);
        }

        var rows = rowsFromCampaigns.Concat(rowsFromOrphans).ToList();

        // ── 4. Resumen (último mensaje saliente del agente) por conversación ─
        var conversationIds = rows.Select(r => r.ConversationId).Distinct().ToList();
        var summaries = await db.Messages
            .Where(m => conversationIds.Contains(m.ConversationId)
                     && m.Direction == MessageDirection.Outbound
                     && m.IsFromAgent)
            .GroupBy(m => m.ConversationId)
            .Select(g => new
            {
                ConvId = g.Key,
                LastContent = g.OrderByDescending(m => m.SentAt).First().Content,
            })
            .ToDictionaryAsync(x => x.ConvId, x => x.LastContent ?? "", ct);

        // ── 5. Mapeo usuarios — para columnas "Usuario" + "Apellido" ──────
        var userIds = campaigns
            .Where(c => !string.IsNullOrEmpty(c.LaunchedByUserId))
            .Select(c => c.LaunchedByUserId!)
            .Distinct()
            .ToList();
        var users = await db.AppUsers
            .Where(u => userIds.Contains(u.Id.ToString()) || userIds.Contains(u.Email))
            .Select(u => new { u.Id, u.FullName, u.Email })
            .ToListAsync(ct);
        var userByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var u in users)
        {
            userByKey[u.Id.ToString()] = u.FullName ?? u.Email ?? "";
            if (!string.IsNullOrEmpty(u.Email))
                userByKey[u.Email] = u.FullName ?? u.Email;
        }

        log.LogInformation(
            "[ConversationDetails] Tenant={Tenant} | {Rows} filas ({Campaigns} de campañas + {Orphans} inbound sin campaña)",
            tenantId, rows.Count, rowsFromCampaigns.Count, rowsFromOrphans.Count);

        // ── 6. Build Excel ───────────────────────────────────────────────
        using var wb = new XLWorkbook();
        var sheet = wb.Worksheets.Add("Detalle Conversaciones");

        var headers = new[] {
            "Campaña", "Cliente", "Celular", "Identificación", "Fecha Gestión",
            "Etiqueta", "Resumen de la conversación", "Usuario", "Apellido", "Agente"
        };
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = sheet.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1E40AF");
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        int row = 2;
        foreach (var r in rows.OrderBy(x => x.CampaignName).ThenByDescending(x => x.LastActivity))
        {
            var (firstName, lastName) = SplitName(ResolveUserName(r.CampaignLaunchedByUserId, userByKey));
            sheet.Cell(row, 1).Value = r.CampaignName ?? "";
            sheet.Cell(row, 2).Value = r.ClientName ?? "";
            sheet.Cell(row, 3).Value = r.ClientPhone ?? "";
            sheet.Cell(row, 4).Value = r.Identification ?? "";
            sheet.Cell(row, 5).Value = (r.LabeledAt ?? r.LastActivity).ToString("dd/MM/yyyy");
            sheet.Cell(row, 6).Value = r.LabelName ?? "";
            sheet.Cell(row, 7).Value = summaries.TryGetValue(r.ConversationId, out var s) ? s : "";
            sheet.Cell(row, 8).Value = firstName;
            sheet.Cell(row, 9).Value = lastName;
            sheet.Cell(row, 10).Value = r.AgentName ?? "";
            row++;
        }
        sheet.Columns().AdjustToContents();
        sheet.SheetView.FreezeRows(1);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static string ResolveUserName(string? launchedByUserId, Dictionary<string, string> userByKey)
    {
        if (string.IsNullOrWhiteSpace(launchedByUserId)) return "";
        if (userByKey.TryGetValue(launchedByUserId, out var name)) return name;
        // Marcadores tipo "system:download" pasan tal cual — el resumen del job
        // nocturno hace lo mismo y operaciones los reconoce.
        return launchedByUserId.StartsWith("system:", StringComparison.OrdinalIgnoreCase) ? launchedByUserId : "";
    }

    /// <summary>
    /// Divide "Nombre Apellido[s]" en dos columnas. La primera palabra es Nombre;
    /// el resto es Apellido. Idéntico al criterio del SendLabelingSummaryExecutor.
    /// </summary>
    private static (string FirstName, string LastName) SplitName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return ("", "");
        var parts = fullName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1) return (parts[0], "");
        return (parts[0], parts[1]);
    }

    /// <summary>POCO interno — evita anonymous-types cuando hay que concatenar dos queries.</summary>
    private sealed class ConvRow
    {
        public string CampaignName { get; init; } = "";
        public string? ClientName { get; init; }
        public string? ClientPhone { get; init; }
        public string? Identification { get; init; }
        public DateTime LastActivity { get; init; }
        public string? LabelName { get; init; }
        public DateTime? LabeledAt { get; init; }
        public Guid ConversationId { get; init; }
        public string? CampaignLaunchedByUserId { get; init; }
        public string? AgentName { get; init; }
    }
}
