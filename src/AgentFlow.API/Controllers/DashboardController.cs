using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using AgentFlow.Application.Modules.Monitor;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Persistence;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.API.Controllers;

[ApiController]
[Authorize]
[Route("api/dashboard")]
public class DashboardController(AgentFlowDbContext db, ITenantContext tenantCtx) : ControllerBase
{
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;

        // Conversaciones activas (no cerradas)
        var totalConversations = await db.Conversations
            .CountAsync(c => c.TenantId == tenantId
                && c.Status != ConversationStatus.Closed
                && c.Status != ConversationStatus.Unresponsive, ct);

        // Agentes activos (distintos agentes usados en conversaciones abiertas)
        var activeAgents = await db.Conversations
            .Where(c => c.TenantId == tenantId
                && c.Status != ConversationStatus.Closed
                && c.ActiveAgentId != null)
            .Select(c => c.ActiveAgentId)
            .Distinct()
            .CountAsync(ct);

        // Campañas activas
        var activeCampaigns = await db.Campaigns
            .CountAsync(c => c.TenantId == tenantId && c.IsActive, ct);

        // Escaladas a humano
        var escalatedCount = await db.Conversations
            .CountAsync(c => c.TenantId == tenantId
                && c.Status == ConversationStatus.EscalatedToHuman, ct);

        // Distribución por resultado de gestión (join con conversación para filtrar por tenant)
        var gestionByResult = await db.GestionEvents
            .Where(g => g.Conversation.TenantId == tenantId)
            .GroupBy(g => g.Result)
            .Select(g => new { Result = g.Key.ToString(), Count = g.Count() })
            .ToListAsync(ct);

        // Conversaciones recientes (últimas 10 con actividad)
        var recentConvs = await db.Conversations
            .Include(c => c.ActiveAgent)
            .Include(c => c.Messages.OrderByDescending(m => m.SentAt).Take(1))
            .Where(c => c.TenantId == tenantId)
            .OrderByDescending(c => c.LastActivityAt)
            .Take(10)
            .ToListAsync(ct);

        var recentConversations = recentConvs.Select(c => new
        {
            id              = c.Id,
            clientPhone     = c.ClientPhone,
            clientName      = c.ClientName,
            agentType       = c.ActiveAgent?.Type.ToString() ?? "General",
            status          = c.Status.ToString(),
            isHumanHandled  = c.IsHumanHandled,
            lastActivityAt  = c.LastActivityAt,
            lastMessagePreview = c.Messages
                .OrderByDescending(m => m.SentAt)
                .FirstOrDefault()?.Content[..Math.Min(80,
                    c.Messages.OrderByDescending(m => m.SentAt)
                        .FirstOrDefault()?.Content.Length ?? 0)]
        });

        return Ok(new
        {
            totalConversations,
            activeAgents,
            activeCampaigns,
            escalatedCount,
            gestionByResult = gestionByResult.ToDictionary(x => x.Result, x => x.Count),
            recentConversations,
        });
    }

    /// <summary>
    /// Distribución de etiquetas IA de las conversaciones del tenant en un mes/año
    /// específico. Por defecto el mes/año actuales en hora de Panamá.
    /// Equivalente al gráfico del correo SEND_LABELING_SUMMARY pero filtrable
    /// por período y consumible desde el dashboard del portal.
    /// </summary>
    [HttpGet("labeling-summary")]
    public async Task<IActionResult> GetLabelingSummary(
        [FromQuery] int? year = null,
        [FromQuery] int? month = null,
        CancellationToken ct = default)
    {
        var tenantId = tenantCtx.TenantId;

        // Defaults: mes y año actuales en hora de Panamá (UTC-5).
        var nowPa = DateTime.UtcNow.AddHours(-5);
        var y = year ?? nowPa.Year;
        var m = month ?? nowPa.Month;
        if (m < 1 || m > 12) return BadRequest(new { error = "Mes inválido (1-12)." });

        // Rango UTC del mes: [primer día 00:00 PA, primer día del mes siguiente 00:00 PA]
        var startPa = new DateTime(y, m, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var endPa = startPa.AddMonths(1);
        var startUtc = startPa.AddHours(5);
        var endUtc = endPa.AddHours(5);

        // Convs procesadas en el período = todas las que tienen LabeledAt en el rango,
        // O sin LabeledAt pero iniciadas (StartedAt) en el rango (para contar
        // "Sin etiqueta" — convs del mes que la IA aún no etiquetó).
        var conversationsInPeriod = await db.Conversations
            .Where(c => c.TenantId == tenantId
                     && (c.LabeledAt != null
                         ? c.LabeledAt >= startUtc && c.LabeledAt < endUtc
                         : c.StartedAt >= startUtc && c.StartedAt < endUtc))
            .Select(c => new { c.Id, c.LabelId, c.CampaignId })
            .ToListAsync(ct);

        var totalConvs = conversationsInPeriod.Count;
        var taggedCount = conversationsInPeriod.Count(c => c.LabelId.HasValue);
        var unlabeledCount = totalConvs - taggedCount;
        var taggedPct = totalConvs == 0 ? 0 : (int)Math.Round(taggedCount * 100.0 / totalConvs);
        var campaignsInReport = conversationsInPeriod
            .Where(c => c.CampaignId.HasValue)
            .Select(c => c.CampaignId!.Value)
            .Distinct()
            .Count();

        // Cuenta por label
        var labelCounts = conversationsInPeriod
            .Where(c => c.LabelId.HasValue)
            .GroupBy(c => c.LabelId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        // Resolver nombres/colores en una sola query
        var labels = await db.Set<Domain.Entities.ConversationLabel>()
            .Where(l => l.TenantId == tenantId && labelCounts.Keys.Contains(l.Id))
            .Select(l => new { l.Id, l.Name, l.Color })
            .ToListAsync(ct);

        var labelMap = labels.ToDictionary(l => l.Id);

        var breakdown = labelCounts
            .Select(kv => new
            {
                labelId = kv.Key,
                name = labelMap.TryGetValue(kv.Key, out var l) ? l.Name : "(eliminada)",
                color = labelMap.TryGetValue(kv.Key, out var l2) ? l2.Color : "#94a3b8",
                count = kv.Value,
                percentage = totalConvs == 0 ? 0 : Math.Round(kv.Value * 100.0 / totalConvs, 1),
            })
            .OrderByDescending(b => b.count)
            .ToList();

        return Ok(new
        {
            year = y,
            month = m,
            totalConversations = totalConvs,
            taggedCount,
            taggedPercentage = taggedPct,
            unlabeledCount,
            unlabeledPercentage = totalConvs == 0 ? 0 : Math.Round(unlabeledCount * 100.0 / totalConvs, 1),
            campaignsInReport,
            breakdown,
        });
    }

    /// <summary>
    /// Informe gerencial comparativo entre varios períodos. Cada período es un
    /// mes completo o una quincena (Q1 = días 1-15, Q2 = 16 a fin de mes).
    ///
    /// Efectividad = (count de etiquetas marcadas como efectivas) / total del período.
    /// Por defecto se identifican como efectivas las etiquetas cuyo nombre contiene
    /// "compromiso", "comprobante", "pago" o "pagó" (case-insensitive). El caller
    /// puede sobrescribir esta selección pasando ?effectiveLabelIds=guid1,guid2.
    /// </summary>
    [HttpGet("management-report")]
    public async Task<IActionResult> GetManagementReport(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] string granularity = "monthly",   // "monthly" | "biweekly"
        [FromQuery] Guid? campaignTemplateId = null,   // filtro opcional por maestro
        [FromQuery] string? effectiveLabelIds = null,  // override opcional, csv de guids
        CancellationToken ct = default)
    {
        var tenantId = tenantCtx.TenantId;
        if (to < from) return BadRequest(new { error = "El rango es inválido (from > to)." });
        if ((to - from).TotalDays > 366) return BadRequest(new { error = "El rango no puede exceder 12 meses." });

        var tenant = await db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.Name })
            .FirstOrDefaultAsync(ct);
        if (tenant is null) return Unauthorized();

        // Si se filtra por maestro, cargar sus LabelIds (subset de etiquetas a mostrar).
        // Si no se filtra, se muestran TODAS las etiquetas del tenant.
        List<Guid>? templateLabelIds = null;
        string? templateName = null;
        if (campaignTemplateId.HasValue)
        {
            var tpl = await db.CampaignTemplates
                .Where(t => t.Id == campaignTemplateId.Value && t.TenantId == tenantId)
                .Select(t => new { t.Name, t.LabelIds })
                .FirstOrDefaultAsync(ct);
            if (tpl is null)
                return NotFound(new { error = "Maestro de campaña no encontrado." });
            templateLabelIds = tpl.LabelIds ?? new List<Guid>();
            templateName = tpl.Name;
        }

        // Hora Panamá: rango UTC del from-to (incluye to completo)
        var startUtc = new DateTime(from.Year, from.Month, from.Day, 0, 0, 0, DateTimeKind.Unspecified).AddHours(5);
        var endUtc   = new DateTime(to.Year, to.Month, to.Day, 0, 0, 0, DateTimeKind.Unspecified).AddDays(1).AddHours(5);

        // Cargar conversaciones del rango — opcionalmente filtradas por maestro
        // (vía join con Campaigns.CampaignTemplateId).
        var convsQuery = db.Conversations
            .Where(c => c.TenantId == tenantId
                     && (c.LabeledAt != null
                         ? c.LabeledAt >= startUtc && c.LabeledAt < endUtc
                         : c.StartedAt >= startUtc && c.StartedAt < endUtc));

        if (campaignTemplateId.HasValue)
        {
            // Solo conversaciones cuya Campaign usa el CampaignTemplate seleccionado.
            // Las conversaciones sueltas (sin Campaign) quedan fuera del filtro.
            var campaignIdsOfTemplate = db.Campaigns
                .Where(c => c.TenantId == tenantId && c.CampaignTemplateId == campaignTemplateId.Value)
                .Select(c => c.Id);
            convsQuery = convsQuery
                .Where(c => c.CampaignId.HasValue && campaignIdsOfTemplate.Contains(c.CampaignId.Value));
        }

        var allConvs = await convsQuery
            .Select(c => new {
                c.Id,
                c.LabelId,
                Date = c.LabeledAt ?? c.StartedAt,
            })
            .ToListAsync(ct);

        // Cargar labels del tenant (filtrar al subset del maestro si aplica)
        var labelsQuery = db.Set<Domain.Entities.ConversationLabel>()
            .Where(l => l.TenantId == tenantId);
        if (templateLabelIds is not null && templateLabelIds.Count > 0)
        {
            var idsCopy = templateLabelIds; // EF translate
            labelsQuery = labelsQuery.Where(l => idsCopy.Contains(l.Id));
        }
        var labels = await labelsQuery
            .Select(l => new { l.Id, l.Name, l.Color })
            .ToListAsync(ct);
        var labelById = labels.ToDictionary(l => l.Id);

        // Determinar etiquetas "efectivas" para el cálculo
        HashSet<Guid> effectiveSet;
        if (!string.IsNullOrWhiteSpace(effectiveLabelIds))
        {
            effectiveSet = effectiveLabelIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => Guid.TryParse(s.Trim(), out var g) ? g : Guid.Empty)
                .Where(g => g != Guid.Empty)
                .ToHashSet();
        }
        else
        {
            // Matching automático por keywords del nombre de la etiqueta.
            // Cubre los casos típicos: "Compromiso de pago", "Envió Comprobante",
            // "Pago recibido", "Pago confirmado". Insensible a mayúsculas y acentos
            // simples (la BD ya guarda con tilde correcta).
            var keywords = new[] { "compromiso", "comprobante", "pago" };
            effectiveSet = labels
                .Where(l => keywords.Any(k =>
                    l.Name.Contains(k, StringComparison.OrdinalIgnoreCase)))
                .Select(l => l.Id)
                .ToHashSet();
        }

        // Generar lista de períodos según granularidad
        var periods = BuildPeriods(from, to, granularity);

        // Por cada período, agrupar y calcular
        var labelIdsSet = new HashSet<Guid>(labels.Select(l => l.Id));
        var periodsResult = periods.Select(p =>
        {
            var rangeStartUtc = new DateTime(p.start.Year, p.start.Month, p.start.Day, 0, 0, 0, DateTimeKind.Unspecified).AddHours(5);
            var rangeEndUtc   = new DateTime(p.end.Year, p.end.Month, p.end.Day, 0, 0, 0, DateTimeKind.Unspecified).AddDays(1).AddHours(5);
            var convs = allConvs.Where(c => c.Date >= rangeStartUtc && c.Date < rangeEndUtc).ToList();
            var total = convs.Count;

            var counts = convs
                .Where(c => c.LabelId.HasValue && labelIdsSet.Contains(c.LabelId.Value))
                .GroupBy(c => c.LabelId!.Value)
                .ToDictionary(g => g.Key, g => g.Count());
            var unlabeled = total - counts.Values.Sum();

            var effectiveCount = counts.Where(kv => effectiveSet.Contains(kv.Key)).Sum(kv => kv.Value);
            var effectivenessPct = total == 0 ? 0 : Math.Round(effectiveCount * 100.0 / total, 1);

            var breakdown = counts
                .Select(kv => new
                {
                    labelId = kv.Key,
                    name = labelById.TryGetValue(kv.Key, out var l) ? l.Name : "(eliminada)",
                    color = labelById.TryGetValue(kv.Key, out var l2) ? l2.Color : "#94a3b8",
                    isEffective = effectiveSet.Contains(kv.Key),
                    count = kv.Value,
                    percentage = total == 0 ? 0 : Math.Round(kv.Value * 100.0 / total, 1),
                })
                .OrderByDescending(b => b.count)
                .ToList();

            return new
            {
                p.label,
                start = p.start,
                end = p.end,
                total,
                effectiveness = effectivenessPct,
                unlabeledCount = unlabeled,
                unlabeledPercentage = total == 0 ? 0 : Math.Round(unlabeled * 100.0 / total, 1),
                breakdown,
            };
        }).ToList();

        // Universo total agregado (todos los períodos) — para la tabla "Grand Total"
        var totalAll = periodsResult.Sum(p => p.total);
        var effectiveAll = periodsResult.Sum(p =>
            p.breakdown.Where(b => b.isEffective).Sum(b => b.count));
        var effectivenessAll = totalAll == 0 ? 0 : Math.Round(effectiveAll * 100.0 / totalAll, 1);

        return Ok(new
        {
            tenantName = tenant.Name,
            from = from.ToString("yyyy-MM-dd"),
            to = to.ToString("yyyy-MM-dd"),
            granularity,
            campaignTemplateId,
            campaignTemplateName = templateName,
            effectiveLabelIds = effectiveSet.ToList(),
            totalAll,
            effectivenessAll,
            periods = periodsResult,
        });
    }

    /// <summary>
    /// Exporta el informe gerencial a Excel (.xlsx) con varias hojas:
    /// 1) Resumen — tabla cruzada períodos × etiquetas + Grand Total
    /// 2) Períodos — desglose por período con métricas
    /// 3) Conversaciones — TODA la data cruda usada en el cálculo (soporte y validación)
    /// 4) Etiquetas — catálogo de etiquetas usadas (id, nombre, color, isEffective)
    /// </summary>
    [HttpGet("management-report/export")]
    public async Task<IActionResult> ExportManagementReport(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] string granularity = "monthly",
        [FromQuery] Guid? campaignTemplateId = null,
        [FromQuery] string? effectiveLabelIds = null,
        CancellationToken ct = default)
    {
        var tenantId = tenantCtx.TenantId;
        if (to < from) return BadRequest(new { error = "El rango es inválido." });
        if ((to - from).TotalDays > 366) return BadRequest(new { error = "El rango no puede exceder 12 meses." });

        var tenant = await db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.Name })
            .FirstOrDefaultAsync(ct);
        if (tenant is null) return Unauthorized();

        // Resolución del maestro (si filtra)
        List<Guid>? templateLabelIds = null;
        string? templateName = null;
        if (campaignTemplateId.HasValue)
        {
            var tpl = await db.CampaignTemplates
                .Where(t => t.Id == campaignTemplateId.Value && t.TenantId == tenantId)
                .Select(t => new { t.Name, t.LabelIds })
                .FirstOrDefaultAsync(ct);
            if (tpl is null) return NotFound(new { error = "Maestro no encontrado." });
            templateLabelIds = tpl.LabelIds ?? new List<Guid>();
            templateName = tpl.Name;
        }

        var startUtc = new DateTime(from.Year, from.Month, from.Day, 0, 0, 0, DateTimeKind.Unspecified).AddHours(5);
        var endUtc   = new DateTime(to.Year, to.Month, to.Day, 0, 0, 0, DateTimeKind.Unspecified).AddDays(1).AddHours(5);

        // Conversaciones del rango (con join opcional a Campaign para nombre)
        var convsQuery = from c in db.Conversations
                         join cmp in db.Campaigns on c.CampaignId equals cmp.Id into cmpJoin
                         from cmp in cmpJoin.DefaultIfEmpty()
                         where c.TenantId == tenantId
                            && (c.LabeledAt != null
                                ? c.LabeledAt >= startUtc && c.LabeledAt < endUtc
                                : c.StartedAt >= startUtc && c.StartedAt < endUtc)
                         select new
                         {
                             c.Id,
                             c.ClientName,
                             c.ClientPhone,
                             c.LabelId,
                             c.LabeledAt,
                             c.StartedAt,
                             c.ClosedAt,
                             c.Status,
                             CampaignName = cmp != null ? cmp.Name : null,
                             CampaignId = cmp != null ? (Guid?)cmp.Id : null,
                             CampaignTemplateId = cmp != null ? cmp.CampaignTemplateId : null,
                         };

        if (campaignTemplateId.HasValue)
        {
            convsQuery = convsQuery.Where(c => c.CampaignTemplateId == campaignTemplateId.Value);
        }

        var allConvs = await convsQuery.ToListAsync(ct);

        // Etiquetas (filtradas a las del maestro si aplica)
        var labelsQuery = db.Set<Domain.Entities.ConversationLabel>()
            .Where(l => l.TenantId == tenantId);
        if (templateLabelIds is not null && templateLabelIds.Count > 0)
        {
            var idsCopy = templateLabelIds;
            labelsQuery = labelsQuery.Where(l => idsCopy.Contains(l.Id));
        }
        var labels = await labelsQuery
            .Select(l => new { l.Id, l.Name, l.Color })
            .ToListAsync(ct);
        var labelById = labels.ToDictionary(l => l.Id);

        // Efectividad
        HashSet<Guid> effectiveSet;
        if (!string.IsNullOrWhiteSpace(effectiveLabelIds))
        {
            effectiveSet = effectiveLabelIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => Guid.TryParse(s.Trim(), out var g) ? g : Guid.Empty)
                .Where(g => g != Guid.Empty).ToHashSet();
        }
        else
        {
            var keywords = new[] { "compromiso", "comprobante", "pago" };
            effectiveSet = labels
                .Where(l => keywords.Any(k => l.Name.Contains(k, StringComparison.OrdinalIgnoreCase)))
                .Select(l => l.Id).ToHashSet();
        }

        var periods = BuildPeriods(from, to, granularity);

        // Generar el Excel
        using var wb = new ClosedXML.Excel.XLWorkbook();

        // ── Hoja 1: Resumen — tabla cruzada períodos x etiquetas ──
        var wsResumen = wb.Worksheets.Add("Resumen");
        wsResumen.Cell(1, 1).Value = $"Informe Gerencial — {tenant.Name}";
        wsResumen.Cell(1, 1).Style.Font.Bold = true;
        wsResumen.Cell(1, 1).Style.Font.FontSize = 14;
        wsResumen.Range(1, 1, 1, 4 + periods.Count * 2).Merge();
        wsResumen.Cell(2, 1).Value = $"Período: {from:yyyy-MM-dd} a {to:yyyy-MM-dd} · Granularidad: {granularity}" +
                                     (templateName is null ? "" : $" · Maestro: {templateName}");
        wsResumen.Cell(2, 1).Style.Font.FontSize = 10;
        wsResumen.Cell(2, 1).Style.Font.FontColor = ClosedXML.Excel.XLColor.Gray;
        wsResumen.Range(2, 1, 2, 4 + periods.Count * 2).Merge();

        // Header de la tabla en fila 4
        var headerRow = 4;
        wsResumen.Cell(headerRow, 1).Value = "Clasificación";
        var col = 2;
        foreach (var p in periods)
        {
            wsResumen.Cell(headerRow, col).Value = p.label;
            wsResumen.Range(headerRow, col, headerRow, col + 1).Merge();
            wsResumen.Cell(headerRow + 1, col).Value = "Cant.";
            wsResumen.Cell(headerRow + 1, col + 1).Value = "%";
            col += 2;
        }
        wsResumen.Cell(headerRow, col).Value = "Total";
        wsResumen.Range(headerRow, col, headerRow, col + 1).Merge();
        wsResumen.Cell(headerRow + 1, col).Value = "Cant.";
        wsResumen.Cell(headerRow + 1, col + 1).Value = "%";

        // Estilo header
        var headerRange = wsResumen.Range(headerRow, 1, headerRow + 1, col + 1);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#dbeafe");
        headerRange.Style.Font.FontColor = ClosedXML.Excel.XLColor.FromHtml("#1e3a8a");

        // Datos por etiqueta
        var dataRow = headerRow + 2;
        var totalsByLabel = new Dictionary<Guid, int>();
        var totalByPeriod = new int[periods.Count];

        // Pre-computar counts por período para reutilizar
        var countsByPeriodLabel = new Dictionary<int, Dictionary<Guid, int>>();
        var unlabeledByPeriod = new Dictionary<int, int>();
        for (int pi = 0; pi < periods.Count; pi++)
        {
            var p = periods[pi];
            var rangeStartUtc = new DateTime(p.start.Year, p.start.Month, p.start.Day, 0, 0, 0, DateTimeKind.Unspecified).AddHours(5);
            var rangeEndUtc   = new DateTime(p.end.Year, p.end.Month, p.end.Day, 0, 0, 0, DateTimeKind.Unspecified).AddDays(1).AddHours(5);
            var convs = allConvs.Where(c =>
            {
                var date = c.LabeledAt ?? c.StartedAt;
                return date >= rangeStartUtc && date < rangeEndUtc;
            }).ToList();

            var labelIdsSet = labels.Select(l => l.Id).ToHashSet();
            var counts = convs
                .Where(c => c.LabelId.HasValue && labelIdsSet.Contains(c.LabelId.Value))
                .GroupBy(c => c.LabelId!.Value)
                .ToDictionary(g => g.Key, g => g.Count());
            countsByPeriodLabel[pi] = counts;
            unlabeledByPeriod[pi] = convs.Count - counts.Values.Sum();
            totalByPeriod[pi] = convs.Count;
        }

        foreach (var lab in labels)
        {
            wsResumen.Cell(dataRow, 1).Value = lab.Name + (effectiveSet.Contains(lab.Id) ? " (efectiva)" : "");
            int totalLabel = 0;
            int c2 = 2;
            for (int pi = 0; pi < periods.Count; pi++)
            {
                var cnt = countsByPeriodLabel[pi].TryGetValue(lab.Id, out var v) ? v : 0;
                totalLabel += cnt;
                wsResumen.Cell(dataRow, c2).Value = cnt;
                var pct = totalByPeriod[pi] == 0 ? 0 : cnt * 100.0 / totalByPeriod[pi];
                wsResumen.Cell(dataRow, c2 + 1).Value = pct / 100.0;
                wsResumen.Cell(dataRow, c2 + 1).Style.NumberFormat.Format = "0.0%";
                c2 += 2;
            }
            totalsByLabel[lab.Id] = totalLabel;
            wsResumen.Cell(dataRow, c2).Value = totalLabel;
            wsResumen.Cell(dataRow, c2).Style.Font.Bold = true;
            var totalAllRow = totalByPeriod.Sum();
            wsResumen.Cell(dataRow, c2 + 1).Value = totalAllRow == 0 ? 0 : totalLabel * 1.0 / totalAllRow;
            wsResumen.Cell(dataRow, c2 + 1).Style.NumberFormat.Format = "0.0%";
            dataRow++;
        }

        // Fila Sin etiqueta
        if (unlabeledByPeriod.Values.Any(u => u > 0))
        {
            wsResumen.Cell(dataRow, 1).Value = "Sin etiqueta";
            wsResumen.Cell(dataRow, 1).Style.Font.Italic = true;
            int totalUn = 0;
            int c3 = 2;
            for (int pi = 0; pi < periods.Count; pi++)
            {
                var u = unlabeledByPeriod[pi];
                totalUn += u;
                wsResumen.Cell(dataRow, c3).Value = u;
                var pct = totalByPeriod[pi] == 0 ? 0 : u * 1.0 / totalByPeriod[pi];
                wsResumen.Cell(dataRow, c3 + 1).Value = pct;
                wsResumen.Cell(dataRow, c3 + 1).Style.NumberFormat.Format = "0.0%";
                c3 += 2;
            }
            wsResumen.Cell(dataRow, c3).Value = totalUn;
            var totalAllRow = totalByPeriod.Sum();
            wsResumen.Cell(dataRow, c3 + 1).Value = totalAllRow == 0 ? 0 : totalUn * 1.0 / totalAllRow;
            wsResumen.Cell(dataRow, c3 + 1).Style.NumberFormat.Format = "0.0%";
            dataRow++;
        }

        // Grand Total
        wsResumen.Cell(dataRow, 1).Value = "Grand Total";
        var totalRange = wsResumen.Range(dataRow, 1, dataRow, 1 + periods.Count * 2 + 2);
        totalRange.Style.Font.Bold = true;
        totalRange.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#eff6ff");
        int cc = 2;
        for (int pi = 0; pi < periods.Count; pi++)
        {
            var effCount = countsByPeriodLabel[pi]
                .Where(kv => effectiveSet.Contains(kv.Key)).Sum(kv => kv.Value);
            var effPct = totalByPeriod[pi] == 0 ? 0 : effCount * 1.0 / totalByPeriod[pi];
            wsResumen.Cell(dataRow, cc).Value = totalByPeriod[pi];
            wsResumen.Cell(dataRow, cc + 1).Value = effPct;
            wsResumen.Cell(dataRow, cc + 1).Style.NumberFormat.Format = "0.0%";
            cc += 2;
        }
        var totalAll = totalByPeriod.Sum();
        var totalEffCount = countsByPeriodLabel.Values.SelectMany(d => d)
            .Where(kv => effectiveSet.Contains(kv.Key)).Sum(kv => kv.Value);
        wsResumen.Cell(dataRow, cc).Value = totalAll;
        wsResumen.Cell(dataRow, cc + 1).Value = totalAll == 0 ? 0 : totalEffCount * 1.0 / totalAll;
        wsResumen.Cell(dataRow, cc + 1).Style.NumberFormat.Format = "0.0%";

        wsResumen.Columns().AdjustToContents();

        // ── Embeber chart (PNG generado server-side) en la misma hoja "Resumen" ──
        // Se inserta a partir de 2 filas debajo del Grand Total. El PNG mide
        // 1100×420 px aprox, suficiente para 3-6 períodos legibles.
        try
        {
            var chartRowStart = dataRow + 3;
            var labelsForChart = labels.Select(l => new ChartLabel(l.Id, l.Name, l.Color, effectiveSet.Contains(l.Id))).ToList();
            var periodsForChart = periods.Select((p, i) => new ChartPeriod(
                p.label,
                totalByPeriod[i],
                effectiveSet.Where(eId => countsByPeriodLabel[i].ContainsKey(eId))
                    .Sum(eId => countsByPeriodLabel[i][eId]),
                countsByPeriodLabel[i],
                unlabeledByPeriod[i]
            )).ToList();
            var pngBytes = GenerateStackedBarChartPng(periodsForChart, labelsForChart, tenant.Name);
            using var pngStream = new MemoryStream(pngBytes);
            var pic = wsResumen.AddPicture(pngStream)
                .MoveTo(wsResumen.Cell(chartRowStart, 1));
            // Escalar para que no sea gigante en pantalla. 1 unidad EMU = 1 px aprox.
            pic.WithSize(900, 350);
        }
        catch (Exception)
        {
            // Si el render del chart falla (GDI+ en server, font missing, etc.) lo
            // dejamos sin imagen — el resto del Excel ya tiene la data completa.
        }

        // ── Hoja 2: Períodos — métricas por período ──
        var wsPeriods = wb.Worksheets.Add("Por período");
        wsPeriods.Cell(1, 1).Value = "Período";
        wsPeriods.Cell(1, 2).Value = "Inicio";
        wsPeriods.Cell(1, 3).Value = "Fin";
        wsPeriods.Cell(1, 4).Value = "Total conversaciones";
        wsPeriods.Cell(1, 5).Value = "Etiquetadas";
        wsPeriods.Cell(1, 6).Value = "Sin etiqueta";
        wsPeriods.Cell(1, 7).Value = "Efectividad %";
        wsPeriods.Row(1).Style.Font.Bold = true;
        wsPeriods.Row(1).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#dbeafe");
        for (int pi = 0; pi < periods.Count; pi++)
        {
            var p = periods[pi];
            var effCount = countsByPeriodLabel[pi].Where(kv => effectiveSet.Contains(kv.Key)).Sum(kv => kv.Value);
            wsPeriods.Cell(pi + 2, 1).Value = p.label;
            wsPeriods.Cell(pi + 2, 2).Value = p.start;
            wsPeriods.Cell(pi + 2, 2).Style.DateFormat.Format = "yyyy-mm-dd";
            wsPeriods.Cell(pi + 2, 3).Value = p.end;
            wsPeriods.Cell(pi + 2, 3).Style.DateFormat.Format = "yyyy-mm-dd";
            wsPeriods.Cell(pi + 2, 4).Value = totalByPeriod[pi];
            wsPeriods.Cell(pi + 2, 5).Value = totalByPeriod[pi] - unlabeledByPeriod[pi];
            wsPeriods.Cell(pi + 2, 6).Value = unlabeledByPeriod[pi];
            wsPeriods.Cell(pi + 2, 7).Value = totalByPeriod[pi] == 0 ? 0 : effCount * 1.0 / totalByPeriod[pi];
            wsPeriods.Cell(pi + 2, 7).Style.NumberFormat.Format = "0.0%";
        }
        wsPeriods.Columns().AdjustToContents();

        // ── Hoja 3: Conversaciones (data cruda — soporte/validación) ──
        var wsConvs = wb.Worksheets.Add("Conversaciones");
        wsConvs.Cell(1, 1).Value = "Id";
        wsConvs.Cell(1, 2).Value = "Cliente";
        wsConvs.Cell(1, 3).Value = "Teléfono";
        wsConvs.Cell(1, 4).Value = "Etiqueta";
        wsConvs.Cell(1, 5).Value = "Fecha etiquetado (Panamá)";
        wsConvs.Cell(1, 6).Value = "Fecha inicio (Panamá)";
        wsConvs.Cell(1, 7).Value = "Fecha cierre (Panamá)";
        wsConvs.Cell(1, 8).Value = "Estado conversación";
        wsConvs.Cell(1, 9).Value = "Campaña";
        wsConvs.Cell(1, 10).Value = "Efectiva";
        wsConvs.Row(1).Style.Font.Bold = true;
        wsConvs.Row(1).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#dbeafe");

        var rowIdx = 2;
        foreach (var c in allConvs.OrderByDescending(c => c.LabeledAt ?? c.StartedAt))
        {
            wsConvs.Cell(rowIdx, 1).Value = c.Id.ToString();
            wsConvs.Cell(rowIdx, 2).Value = c.ClientName ?? "";
            wsConvs.Cell(rowIdx, 3).Value = c.ClientPhone ?? "";
            wsConvs.Cell(rowIdx, 4).Value = c.LabelId.HasValue && labelById.TryGetValue(c.LabelId.Value, out var l) ? l.Name : "(sin etiqueta)";
            if (c.LabeledAt.HasValue)
            {
                wsConvs.Cell(rowIdx, 5).Value = c.LabeledAt.Value.AddHours(-5);
                wsConvs.Cell(rowIdx, 5).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
            }
            wsConvs.Cell(rowIdx, 6).Value = c.StartedAt.AddHours(-5);
            wsConvs.Cell(rowIdx, 6).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
            if (c.ClosedAt.HasValue)
            {
                wsConvs.Cell(rowIdx, 7).Value = c.ClosedAt.Value.AddHours(-5);
                wsConvs.Cell(rowIdx, 7).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
            }
            wsConvs.Cell(rowIdx, 8).Value = c.Status.ToString();
            wsConvs.Cell(rowIdx, 9).Value = c.CampaignName ?? "(sin campaña)";
            wsConvs.Cell(rowIdx, 10).Value = c.LabelId.HasValue && effectiveSet.Contains(c.LabelId.Value) ? "Sí" : "No";
            rowIdx++;
        }
        wsConvs.Columns().AdjustToContents();
        wsConvs.Column(1).Width = 14;
        wsConvs.SheetView.FreezeRows(1);

        // ── Hoja 4: Etiquetas (catálogo) ──
        var wsLabels = wb.Worksheets.Add("Etiquetas");
        wsLabels.Cell(1, 1).Value = "Id";
        wsLabels.Cell(1, 2).Value = "Nombre";
        wsLabels.Cell(1, 3).Value = "Color";
        wsLabels.Cell(1, 4).Value = "Efectiva";
        wsLabels.Cell(1, 5).Value = "Total casos";
        wsLabels.Row(1).Style.Font.Bold = true;
        wsLabels.Row(1).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#dbeafe");
        var rowL = 2;
        foreach (var lab in labels)
        {
            wsLabels.Cell(rowL, 1).Value = lab.Id.ToString();
            wsLabels.Cell(rowL, 2).Value = lab.Name;
            wsLabels.Cell(rowL, 3).Value = lab.Color;
            wsLabels.Cell(rowL, 4).Value = effectiveSet.Contains(lab.Id) ? "Sí" : "No";
            wsLabels.Cell(rowL, 5).Value = totalsByLabel.TryGetValue(lab.Id, out var t) ? t : 0;
            try
            {
                wsLabels.Cell(rowL, 3).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml(lab.Color);
            }
            catch { }
            rowL++;
        }
        wsLabels.Columns().AdjustToContents();
        wsLabels.Column(1).Width = 14;

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        var bytes = ms.ToArray();

        var safeTenant = string.Join("_", tenant.Name.Split(Path.GetInvalidFileNameChars()));
        var fileName = $"informe_gerencial_{safeTenant}_{from:yyyyMMdd}_{to:yyyyMMdd}.xlsx";
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    // Genera lista de "períodos" entre from y to según granularidad.
    // - monthly: un período por cada mes completo (recorta extremos si el rango parcial).
    // - biweekly: dos períodos por mes (Q1 = 1-15, Q2 = 16-fin de mes), recortado a from/to.
    private static List<(string label, DateTime start, DateTime end)> BuildPeriods(
        DateTime from, DateTime to, string granularity)
    {
        var result = new List<(string label, DateTime start, DateTime end)>();
        var fromDate = from.Date;
        var toDate = to.Date;

        var monthNames = new[] { "", "Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio",
                                  "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre" };

        if (string.Equals(granularity, "biweekly", StringComparison.OrdinalIgnoreCase))
        {
            // Empezar desde el primer día del mes de "from" (no necesariamente día 1 ni 16)
            var cursor = new DateTime(fromDate.Year, fromDate.Month, fromDate.Day);
            while (cursor <= toDate)
            {
                var month = cursor.Month;
                var year = cursor.Year;
                var lastOfMonth = new DateTime(year, month, DateTime.DaysInMonth(year, month));

                // Q1: 1-15, Q2: 16-fin
                DateTime qStart, qEnd;
                string qLabel;
                if (cursor.Day <= 15)
                {
                    qStart = new DateTime(year, month, Math.Max(1, cursor.Day));
                    qEnd = new DateTime(year, month, 15);
                    if (qEnd > toDate) qEnd = toDate;
                    qLabel = $"{monthNames[month]} {year} — Q1";
                }
                else
                {
                    qStart = new DateTime(year, month, Math.Max(16, cursor.Day));
                    qEnd = lastOfMonth;
                    if (qEnd > toDate) qEnd = toDate;
                    qLabel = $"{monthNames[month]} {year} — Q2";
                }

                result.Add((qLabel, qStart, qEnd));

                // Avanzar cursor al siguiente quincena
                if (qEnd == lastOfMonth) cursor = lastOfMonth.AddDays(1);
                else if (qEnd.Day == 15) cursor = new DateTime(year, month, 16);
                else cursor = qEnd.AddDays(1);
            }
        }
        else
        {
            // monthly: un período por mes que toque el rango (recortado a from/to)
            var cursor = new DateTime(fromDate.Year, fromDate.Month, 1);
            while (cursor <= toDate)
            {
                var month = cursor.Month;
                var year = cursor.Year;
                var firstOfMonth = new DateTime(year, month, 1);
                var lastOfMonth = new DateTime(year, month, DateTime.DaysInMonth(year, month));

                var start = firstOfMonth < fromDate ? fromDate : firstOfMonth;
                var end = lastOfMonth > toDate ? toDate : lastOfMonth;
                result.Add(($"{monthNames[month]} {year}", start, end));

                cursor = lastOfMonth.AddDays(1);
            }
        }

        return result;
    }

    // ─── Soporte para chart PNG en Excel ────────────────────────────────────────
    private record ChartLabel(Guid Id, string Name, string ColorHex, bool IsEffective);
    private record ChartPeriod(
        string Label,
        int Total,
        int EffectiveCount,
        Dictionary<Guid, int> CountsByLabel,
        int UnlabeledCount);

    /// <summary>
    /// Genera un PNG con un gráfico de barras horizontales 100% stacked similar al
    /// del ejemplo (RESUMEN RESULTADOS). Una barra por período, segmentos por
    /// etiqueta con su color, etiqueta "Efectividad X%" sobre cada barra.
    /// Usa System.Drawing.Common — requiere Windows (Smartasp lo soporta).
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static byte[] GenerateStackedBarChartPng(
        List<ChartPeriod> periods,
        List<ChartLabel> labels,
        string tenantName)
    {
        const int width = 1600;
        const int barHeight = 56;
        const int gap = 38;
        const int leftLabel = 220;
        const int rightPad = 30;
        const int topPad = 90;
        const int bottomPad = 110;
        var height = topPad + periods.Count * (barHeight + gap) + bottomPad;

        var unlabeledColor = ColorTranslator.FromHtml("#cbd5e1");

        using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(Color.White);

        // Título
        using var titleFont = new Font("Segoe UI", 22, FontStyle.Bold);
        using var titleBrush = new SolidBrush(ColorTranslator.FromHtml("#1d4ed8"));
        var title = $"RESUMEN RESULTADOS — {tenantName.ToUpperInvariant()}";
        var titleSize = g.MeasureString(title, titleFont);
        g.DrawString(title, titleFont, titleBrush, (width - titleSize.Width) / 2, 18);

        var chartLeft = leftLabel;
        var chartRight = width - rightPad;
        var chartWidth = chartRight - chartLeft;

        // Ticks 0..100
        using var tickPen = new Pen(ColorTranslator.FromHtml("#e5e7eb"), 1);
        using var tickDashPen = new Pen(ColorTranslator.FromHtml("#e5e7eb"), 1) { DashStyle = DashStyle.Dash };
        using var tickFont = new Font("Segoe UI", 10);
        using var tickBrush = new SolidBrush(ColorTranslator.FromHtml("#9ca3af"));
        for (int t = 0; t <= 100; t += 10)
        {
            var x = chartLeft + (t / 100f) * chartWidth;
            var pen = (t == 0 || t == 100) ? tickPen : tickDashPen;
            g.DrawLine(pen, x, topPad - 10, x, topPad + periods.Count * (barHeight + gap));
            var tStr = $"{t}%";
            var tSize = g.MeasureString(tStr, tickFont);
            g.DrawString(tStr, tickFont, tickBrush, x - tSize.Width / 2, topPad + periods.Count * (barHeight + gap) + 4);
        }

        // Fonts para barras
        using var periodFont = new Font("Segoe UI", 13, FontStyle.Bold);
        using var periodBrush = new SolidBrush(ColorTranslator.FromHtml("#374151"));
        using var effFont = new Font("Segoe UI", 12, FontStyle.Bold);
        using var effBrush = new SolidBrush(ColorTranslator.FromHtml("#0f172a"));
        using var segFont = new Font("Segoe UI", 11, FontStyle.Bold);

        // Barras
        for (int pi = 0; pi < periods.Count; pi++)
        {
            var p = periods[pi];
            var yTop = topPad + pi * (barHeight + gap);

            // Label del período (multilínea si tiene " — ")
            var lines = p.Label.Split(" — ");
            for (int li = 0; li < lines.Length; li++)
            {
                var ln = lines[li];
                var sz = g.MeasureString(ln, periodFont);
                g.DrawString(ln, periodFont, periodBrush,
                    chartLeft - 12 - sz.Width,
                    yTop + (barHeight - sz.Height * lines.Length) / 2 + li * sz.Height);
            }

            // Efectividad arriba
            var effPct = p.Total == 0 ? 0 : (int)Math.Round(p.EffectiveCount * 100.0 / p.Total);
            var effStr = $"Efectividad {effPct}%";
            g.DrawString(effStr, effFont, effBrush, chartLeft + 4, yTop - 24);

            if (p.Total == 0)
            {
                // Barra vacía con borde
                using var emptyPen = new Pen(ColorTranslator.FromHtml("#e5e7eb"), 1);
                g.DrawRectangle(emptyPen, chartLeft, yTop, chartWidth, barHeight);
                continue;
            }

            // Segmentos: primero las labels ordenadas, después "Sin etiqueta"
            float xCursor = chartLeft;
            foreach (var lab in labels)
            {
                if (!p.CountsByLabel.TryGetValue(lab.Id, out var cnt) || cnt == 0) continue;
                var pct = cnt * 100f / p.Total;
                var w = (pct / 100f) * chartWidth;
                Color color;
                try { color = ColorTranslator.FromHtml(lab.ColorHex); }
                catch { color = ColorTranslator.FromHtml("#94a3b8"); }
                using var brush = new SolidBrush(color);
                g.FillRectangle(brush, xCursor, yTop, w, barHeight);
                if (pct >= 4)
                {
                    var label = $"{Math.Round(pct)}%";
                    var sz = g.MeasureString(label, segFont);
                    using var fg = new SolidBrush(IsDark(color) ? Color.White : ColorTranslator.FromHtml("#0f172a"));
                    g.DrawString(label, segFont, fg, xCursor + w / 2 - sz.Width / 2, yTop + barHeight / 2 - sz.Height / 2);
                }
                xCursor += w;
            }

            // Sin etiqueta
            if (p.UnlabeledCount > 0)
            {
                var pct = p.UnlabeledCount * 100f / p.Total;
                var w = (pct / 100f) * chartWidth;
                using var brush = new SolidBrush(unlabeledColor);
                g.FillRectangle(brush, xCursor, yTop, w, barHeight);
                if (pct >= 4)
                {
                    var label = $"{Math.Round(pct)}%";
                    var sz = g.MeasureString(label, segFont);
                    using var fg = new SolidBrush(ColorTranslator.FromHtml("#0f172a"));
                    g.DrawString(label, segFont, fg, xCursor + w / 2 - sz.Width / 2, yTop + barHeight / 2 - sz.Height / 2);
                }
                xCursor += w;
            }
        }

        // Leyenda (debajo de los ticks)
        var legendY = height - bottomPad + 50;
        float legendX = chartLeft;
        using var legendFont = new Font("Segoe UI", 10);
        using var legendBrush = new SolidBrush(ColorTranslator.FromHtml("#374151"));
        var legendItems = labels
            .Select(l => (Name: l.Name, ColorHex: l.ColorHex))
            .Append((Name: "Sin etiqueta", ColorHex: "#cbd5e1"))
            .ToList();
        foreach (var item in legendItems)
        {
            Color color;
            try { color = ColorTranslator.FromHtml(item.ColorHex); } catch { color = unlabeledColor; }
            using var br = new SolidBrush(color);
            g.FillRectangle(br, legendX, legendY + 4, 12, 12);
            var sz = g.MeasureString(item.Name, legendFont);
            g.DrawString(item.Name, legendFont, legendBrush, legendX + 18, legendY);
            legendX += 18 + sz.Width + 20;
            if (legendX > chartRight - 100)
            {
                legendX = chartLeft;
                legendY += 24;
            }
        }

        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static bool IsDark(Color c)
    {
        var lum = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;
        return lum < 0.55;
    }
}
