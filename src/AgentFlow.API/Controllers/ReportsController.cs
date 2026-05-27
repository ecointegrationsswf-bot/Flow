using AgentFlow.Application.Modules.Reports;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Persistence;
using AgentFlow.Infrastructure.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.API.Controllers;



/// <summary>
/// Reportes operacionales del tenant. Inicialmente expone el reporte de
/// efectividad por cliente único; se diseñó la ruta como <c>/api/reports/...</c>
/// para que crezca con más reportes (renovaciones, reclamos, NPS, etc.)
/// reutilizando la misma página de "Informes" del portal.
///
/// Salidas:
///  - <c>GET /api/reports/effectiveness</c> → JSON (vista previa en la UI).
///  - <c>GET /api/reports/effectiveness/pdf</c> → application/pdf (descarga).
///
/// Multitenant: el <c>tenantId</c> se toma SIEMPRE del <c>ITenantContext</c>
/// resuelto por el middleware desde el JWT del usuario. NO se acepta tenantId
/// por query string para evitar cross-tenant leaks.
/// </summary>
[ApiController]
[Authorize]
[Route("api/reports")]
public class ReportsController(
    IEffectivenessReportService reportService,
    ITenantContext tenantCtx,
    AgentFlowDbContext db,
    IHttpClientFactory httpFactory,
    ILogger<ReportsController> log,
    ConversationDetailsExcelExporter conversationDetailsExporter) : ControllerBase
{
    private const int MaxRangeDays = 366; // máximo 1 año por reporte
    private const int LogoMaxBytes = 2 * 1024 * 1024; // 2 MB cap para evitar logos absurdos

    /// <summary>
    /// Reporte de efectividad en JSON. Lo usa la página de Informes para mostrar
    /// la vista previa antes de descargar el PDF. El filtro opcional es por
    /// <b>maestros de campaña</b> (CampaignTemplate), no por campañas individuales.
    /// </summary>
    [HttpGet("effectiveness")]
    public async Task<IActionResult> Effectiveness(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] Guid[]? campaignTemplateIds,
        CancellationToken ct)
    {
        var validation = ValidateRange(from, to);
        if (validation is not null) return BadRequest(new { error = validation });

        var tenantId = tenantCtx.TenantId;
        if (tenantId == Guid.Empty)
            return BadRequest(new { error = "TenantId no resuelto." });

        var report = await reportService.GenerateAsync(
            tenantId, from.ToUniversalTime(), to.ToUniversalTime(), campaignTemplateIds, ct);

        return Ok(report);
    }

    /// <summary>
    /// Mismo reporte pero generado como PDF nativo (QuestPDF). Devuelve un stream
    /// <c>application/pdf</c> con header <c>Content-Disposition</c> para que el
    /// browser descargue el archivo con un nombre legible.
    /// </summary>
    [HttpGet("effectiveness/pdf")]
    public async Task<IActionResult> EffectivenessPdf(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] Guid[]? campaignTemplateIds,
        CancellationToken ct)
    {
        var validation = ValidateRange(from, to);
        if (validation is not null) return BadRequest(new { error = validation });

        var tenantId = tenantCtx.TenantId;
        if (tenantId == Guid.Empty)
            return BadRequest(new { error = "TenantId no resuelto." });

        var report = await reportService.GenerateAsync(
            tenantId, from.ToUniversalTime(), to.ToUniversalTime(), campaignTemplateIds, ct);

        // Descargamos el logo del tenant (si está configurado) ANTES de generar el
        // PDF para no acoplar el generator (CPU-bound) a la red. Si la descarga
        // falla por cualquier motivo (URL caída, timeout, content-type inválido),
        // generamos el PDF sin logo — el branding es nice-to-have, no bloqueante.
        var logoBytes = await TryDownloadLogoAsync(report.Filters.TenantLogoUrl, ct);

        var generator = new EffectivenessReportPdfGenerator();
        var bytes = generator.Generate(report, logoBytes);

        var tenantSlug = (report.Filters.TenantName ?? "tenant")
            .ToLowerInvariant()
            .Replace(" ", "-");
        var filename = $"informe-efectividad_{tenantSlug}_{from:yyyyMMdd}-{to:yyyyMMdd}.pdf";

        return File(bytes, "application/pdf", filename);
    }

    /// <summary>
    /// Descarga el logo del tenant en bytes para inyectar en el header del PDF.
    /// Best-effort: cualquier fallo (URL nula, timeout, 4xx, payload demasiado
    /// grande) se loguea como warning y retorna null. El PDF se renderiza igual
    /// sin logo. NO propagamos la excepción al cliente.
    /// </summary>
    private async Task<byte[]?> TryDownloadLogoAsync(string? logoUrl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(logoUrl)) return null;
        if (!Uri.TryCreate(logoUrl, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;

        try
        {
            using var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(5);
            using var resp = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
            {
                log.LogWarning("[ReportsController] Logo HTTP {Status} en {Url}", (int)resp.StatusCode, logoUrl);
                return null;
            }
            if (resp.Content.Headers.ContentLength is long len && len > LogoMaxBytes)
            {
                log.LogWarning("[ReportsController] Logo demasiado grande ({Size} bytes) en {Url}", len, logoUrl);
                return null;
            }
            var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length == 0 || bytes.Length > LogoMaxBytes) return null;
            return bytes;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "[ReportsController] Falló descarga de logo {Url}", logoUrl);
            return null;
        }
    }

    /// <summary>
    /// Listado de <b>maestros de campaña</b> (CampaignTemplate) del tenant que
    /// tienen al menos una campaña creada dentro del rango. Pobla el filtro
    /// multi-select del frontend con el universo relevante — un maestro sin
    /// corridas en el rango no tiene sentido mostrarlo.
    ///
    /// Devuelve también <c>campaignCount</c> y <c>totalContacts</c> agregados
    /// para que el usuario sepa cuántas corridas ha tenido el maestro y a
    /// cuántos contactos llegó en el periodo.
    /// </summary>
    [HttpGet("templates-for-filter")]
    public async Task<IActionResult> TemplatesForFilter(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        var tenantId = tenantCtx.TenantId;
        if (tenantId == Guid.Empty)
            return BadRequest(new { error = "TenantId no resuelto." });

        // Universo: campañas del tenant en el rango opcional, agrupadas por su maestro.
        var campaignsQ = db.Campaigns.AsNoTracking().Where(c => c.TenantId == tenantId);
        if (from.HasValue) campaignsQ = campaignsQ.Where(c => c.CreatedAt >= from.Value.ToUniversalTime());
        if (to.HasValue)   campaignsQ = campaignsQ.Where(c => c.CreatedAt <= to.Value.ToUniversalTime());

        var grouped = await campaignsQ
            .Where(c => c.CampaignTemplateId != null)
            .GroupBy(c => c.CampaignTemplateId!.Value)
            .Select(g => new {
                CampaignTemplateId = g.Key,
                CampaignCount = g.Count(),
                TotalContacts = g.Sum(c => (int?)c.TotalContacts) ?? 0,
                LastCreatedAt = g.Max(c => c.CreatedAt),
            })
            .ToListAsync(ct);

        var templateIds = grouped.Select(g => g.CampaignTemplateId).ToList();

        // Resolvemos el nombre del maestro en un join post-grupo.
        var templates = await db.CampaignTemplates.AsNoTracking()
            .Where(t => t.TenantId == tenantId && templateIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Name })
            .ToListAsync(ct);

        var items = (
            from g in grouped
            join t in templates on g.CampaignTemplateId equals t.Id
            orderby g.LastCreatedAt descending
            select new {
                Id = t.Id,
                Name = t.Name,
                CampaignCount = g.CampaignCount,
                TotalContacts = g.TotalContacts,
                LastCreatedAt = g.LastCreatedAt
            }
        ).ToList();

        return Ok(items);
    }

    /// <summary>
    /// Excel "Detalle de Conversaciones" — mismo formato que el resumen que el
    /// job nocturno envía por email cada mañana, pero a demanda con filtros del
    /// usuario. Útil cuando un supervisor necesita re-bajar el detalle de un
    /// rango específico o filtrar por un maestro concreto sin esperar al envío
    /// automático del día siguiente.
    ///
    /// Filtros:
    ///  - <c>from</c> / <c>to</c>: rango de fechas (obligatorio, máx 366 días).
    ///  - <c>campaignTemplateId</c>: maestro de campaña (opcional — null = todos).
    ///  - <c>includeInboundWithoutCampaign</c>: si true, agrega conversaciones
    ///    sin campaña donde el cliente nos escribió espontáneamente en el rango.
    /// </summary>
    [HttpGet("conversation-details/export")]
    public async Task<IActionResult> ConversationDetailsExport(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] Guid? campaignTemplateId,
        [FromQuery] bool includeInboundWithoutCampaign = false,
        CancellationToken ct = default)
    {
        var validation = ValidateRange(from, to);
        if (validation is not null) return BadRequest(new { error = validation });

        var tenantId = tenantCtx.TenantId;
        if (tenantId == Guid.Empty)
            return BadRequest(new { error = "TenantId no resuelto." });

        if (campaignTemplateId.HasValue)
        {
            var tplExists = await db.CampaignTemplates
                .AnyAsync(t => t.Id == campaignTemplateId.Value && t.TenantId == tenantId, ct);
            if (!tplExists) return NotFound(new { error = "Maestro de campaña no encontrado." });
        }

        var bytes = await conversationDetailsExporter.GenerateAsync(
            tenantId,
            from.ToUniversalTime(),
            to.ToUniversalTime(),
            campaignTemplateId,
            includeInboundWithoutCampaign,
            ct);

        var tenantNameOrSlug = await db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => string.IsNullOrEmpty(t.Slug) ? t.Name : t.Slug)
            .FirstOrDefaultAsync(ct) ?? "tenant";
        var tenantSlug = tenantNameOrSlug.ToLowerInvariant().Replace(" ", "-");
        var filename = $"detalle-conversaciones_{tenantSlug}_{from:yyyyMMdd}-{to:yyyyMMdd}.xlsx";

        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            filename);
    }

    private static string? ValidateRange(DateTime from, DateTime to)
    {
        if (to < from) return "El rango es inválido: 'to' es anterior a 'from'.";
        if ((to - from).TotalDays > MaxRangeDays)
            return $"Rango demasiado grande (máximo {MaxRangeDays} días). Reducí el periodo.";
        if (from.Year < 2020) return "Fecha 'from' fuera de rango válido.";
        return null;
    }
}
