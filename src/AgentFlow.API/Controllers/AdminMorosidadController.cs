using System.Text.Json;
using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Enums;
using AgentFlow.Infrastructure.Persistence;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.API.Controllers;

/// <summary>
/// Endpoints de morosidad accesibles por el Super-Admin.
/// Permiten configurar ActionDelinquencyConfig y ActionFieldMappings
/// para cualquier tenant sin necesitar el JWT de ese tenant.
/// </summary>
[ApiController]
[Route("api/admin/morosidad")]
[Authorize(Roles = "super_admin")]
public class AdminMorosidadController(AgentFlowDbContext db) : ControllerBase
{
    // ── Catálogo de campos lógicos (global, sin tenant) ──────────────────────

    [HttpGet("fields")]
    public async Task<IActionResult> GetFieldCatalog(CancellationToken ct)
    {
        var fields = await db.LogicalFieldCatalog
            .Where(f => f.IsActive)
            .OrderBy(f => f.SortOrder)
            .Select(f => new { f.Id, f.Key, f.DisplayName, f.Description, f.DataType, f.IsRequired })
            .ToListAsync(ct);

        return Ok(fields);
    }

    // ── Todas las acciones globales (para configurar morosidad desde admin) ──
    // No filtra por AssignedActionIds — el admin puede configurar cualquier
    // acción global para cualquier tenant (independientemente de la asignación).

    [HttpGet("{tenantId:guid}/actions")]
    public async Task<IActionResult> GetTenantActions(Guid tenantId, CancellationToken ct)
    {
        // Verificar que el tenant exista
        var tenantExists = await db.Tenants.AnyAsync(t => t.Id == tenantId, ct);
        if (!tenantExists) return NotFound("Tenant no encontrado.");

        // Sólo acciones globales marcadas como "descarga de morosidad" — el módulo
        // /admin/morosidad sólo aplica a este tipo de acción (las demás no tienen
        // ActionDelinquencyConfig ni se procesan con DelinquencyProcessor).
        var actions = await db.ActionDefinitions
            .Where(a => a.TenantId == null && a.IsDelinquencyDownload && a.IsActive)
            .OrderBy(a => a.Description ?? a.Name)
            .Select(a => new { a.Id, a.Name, a.Description, a.SendsEmail })
            .ToListAsync(ct);

        return Ok(actions);
    }

    // ── Configuración de morosidad ───────────────────────────────────────────

    [HttpGet("{tenantId:guid}/config/{actionId:guid}")]
    public async Task<IActionResult> GetConfig(Guid tenantId, Guid actionId, CancellationToken ct)
    {
        var config = await db.ActionDelinquencyConfigs
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.ActionDefinitionId == actionId, ct);

        if (config == null) return NotFound();

        return Ok(new
        {
            config.Id,
            config.ActionDefinitionId,
            config.CodigoPais,
            config.ItemsJsonPath,
            config.AutoCrearCampanas,
            config.CampaignTemplateId,
            config.AgentDefinitionId,
            config.CampaignNamePattern,
            config.NotificationEmail,
            config.DownloadWebhookUrl,
            config.DownloadWebhookMethod,
            config.DownloadWebhookHeaders,
            config.IsActive
        });
    }

    [HttpPut("{tenantId:guid}/config/{actionId:guid}")]
    public async Task<IActionResult> UpsertConfig(
        Guid tenantId, Guid actionId,
        [FromBody] AdminUpsertConfigRequest req,
        CancellationToken ct)
    {
        var existing = await db.ActionDelinquencyConfigs
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.ActionDefinitionId == actionId, ct);

        if (existing == null)
        {
            existing = new ActionDelinquencyConfig
            {
                Id                 = Guid.NewGuid(),
                TenantId           = tenantId,
                ActionDefinitionId = actionId,
                CreatedAt          = DateTime.UtcNow
            };
            db.ActionDelinquencyConfigs.Add(existing);
        }

        existing.CodigoPais             = req.CodigoPais ?? "507";
        existing.ItemsJsonPath          = req.ItemsJsonPath;
        existing.AutoCrearCampanas      = req.AutoCrearCampanas;
        existing.CampaignTemplateId     = req.CampaignTemplateId;
        existing.AgentDefinitionId      = req.AgentDefinitionId;
        existing.CampaignNamePattern    = req.CampaignNamePattern;
        existing.NotificationEmail      = req.NotificationEmail;
        existing.DownloadWebhookUrl     = req.DownloadWebhookUrl;
        existing.DownloadWebhookMethod  = req.DownloadWebhookMethod ?? "GET";
        existing.DownloadWebhookHeaders = req.DownloadWebhookHeaders;
        existing.IsActive               = req.IsActive;
        existing.UpdatedAt           = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return Ok(new { existing.Id });
    }

    // ── Mapeo de campos (sin tenant, solo por ActionDefinitionId) ────────────

    [HttpGet("mappings/{actionId:guid}")]
    public async Task<IActionResult> GetMappings(Guid actionId, CancellationToken ct)
    {
        var mappings = await db.ActionFieldMappings
            .Where(m => m.ActionDefinitionId == actionId)
            .OrderBy(m => m.SortOrder)
            .Select(m => new
            {
                m.Id,
                m.ColumnKey,
                m.DisplayName,
                m.JsonPath,
                Role = m.Role.ToString(),
                m.RoleLabel,
                m.DataType,
                m.SortOrder,
                m.DefaultValue,
                m.IsEnabled
            })
            .ToListAsync(ct);

        return Ok(mappings);
    }

    [HttpPut("mappings/{actionId:guid}")]
    public async Task<IActionResult> SetMappings(
        Guid actionId,
        [FromBody] AdminSetMappingsRequest req,
        CancellationToken ct)
    {
        var actionExists = await db.ActionDefinitions.AnyAsync(a => a.Id == actionId, ct);
        if (!actionExists) return NotFound("Acción no encontrada.");

        // Reusar el mismo normalizador + validador del controller tenant.
        var asPortable = (req.Mappings ?? [])
            .Select(m => new MappingItem(
                m.ColumnKey, m.DisplayName, m.JsonPath, m.Role, m.RoleLabel,
                m.DataType, m.SortOrder, m.DefaultValue, m.IsEnabled))
            .ToList();

        var normalized = MorosidadController.NormalizeColumnKeys(asPortable);
        var validation = MorosidadController.ValidateMappings(normalized);
        if (validation != null) return BadRequest(new { error = validation });

        var existing = await db.ActionFieldMappings
            .Where(m => m.ActionDefinitionId == actionId)
            .ToListAsync(ct);

        db.ActionFieldMappings.RemoveRange(existing);

        var newMappings = normalized.Select((m, idx) => new ActionFieldMapping
        {
            Id                 = Guid.NewGuid(),
            ActionDefinitionId = actionId,
            ColumnKey          = m.ColumnKey.Trim(),
            DisplayName        = m.DisplayName.Trim(),
            JsonPath           = m.JsonPath.Trim(),
            Role               = Enum.TryParse<FieldRole>(m.Role, ignoreCase: true, out var r) ? r : FieldRole.None,
            RoleLabel          = string.IsNullOrWhiteSpace(m.RoleLabel) ? null : m.RoleLabel.Trim(),
            DataType           = string.IsNullOrWhiteSpace(m.DataType) ? "string" : m.DataType.Trim(),
            SortOrder          = m.SortOrder ?? idx,
            DefaultValue       = m.DefaultValue,
            IsEnabled          = m.IsEnabled,
            CreatedAt          = DateTime.UtcNow
        }).ToList();

        db.ActionFieldMappings.AddRange(newMappings);
        await db.SaveChangesAsync(ct);

        return Ok(new { saved = newMappings.Count });
    }

    // ── Historial de ejecuciones (read-only, con tenantId explícito) ─────────

    [HttpGet("{tenantId:guid}/executions")]
    public async Task<IActionResult> ListExecutions(
        Guid tenantId,
        [FromQuery] Guid? actionId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var q = db.DelinquencyExecutions.Where(e => e.TenantId == tenantId);
        if (actionId.HasValue) q = q.Where(e => e.ActionDefinitionId == actionId.Value);

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(e => e.StartedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new
            {
                e.Id, e.ActionDefinitionId, e.Status, e.StartedAt, e.CompletedAt,
                e.TotalItems, e.ProcessedItems, e.DiscardedItems,
                e.GroupsCreated, e.CampaignsCreated, e.ErrorMessage
            })
            .ToListAsync(ct);

        return Ok(new { total, page, pageSize, items });
    }

    /// <summary>Lista los grupos de contacto de una ejecución (admin, con tenantId explícito).</summary>
    [HttpGet("{tenantId:guid}/executions/{executionId:guid}/groups")]
    public async Task<IActionResult> ListGroups(
        Guid tenantId,
        Guid executionId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        var executionExists = await db.DelinquencyExecutions
            .AnyAsync(e => e.Id == executionId && e.TenantId == tenantId, ct);
        if (!executionExists) return NotFound();

        var q = db.ContactGroups.Where(g => g.ExecutionId == executionId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(g =>
                (g.ClientName != null && g.ClientName.ToLower().Contains(s)) ||
                g.PhoneNormalized.Contains(s));
        }

        var total = await q.CountAsync(ct);
        var groups = await q
            .OrderBy(g => g.ClientName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(g => new
            {
                g.Id,
                g.PhoneNormalized,
                g.ClientName,
                g.TotalAmount,
                g.ItemCount,
                g.Status,
                g.CampaignId,
                g.CreatedAt,
                g.FirstMessageSentAt,
                g.FirstClientReplyAt
            })
            .ToListAsync(ct);

        return Ok(new { total, page, pageSize, groups });
    }

    /// <summary>Lista los ítems (pólizas) de un grupo específico.</summary>
    [HttpGet("{tenantId:guid}/executions/{executionId:guid}/groups/{groupId:guid}/items")]
    public async Task<IActionResult> ListGroupItems(
        Guid tenantId,
        Guid executionId,
        Guid groupId,
        CancellationToken ct = default)
    {
        var groupExists = await db.ContactGroups
            .AnyAsync(g => g.Id == groupId && g.ExecutionId == executionId && g.TenantId == tenantId, ct);
        if (!groupExists) return NotFound();

        var items = await db.DelinquencyItems
            .Where(i => i.GroupId == groupId)
            .OrderBy(i => i.RowIndex)
            .Select(i => new
            {
                i.Id,
                i.RowIndex,
                i.PolicyNumber,
                i.KeyValue,
                i.Amount,
                i.ClientName,
                i.PhoneRaw,
                i.PhoneNormalized,
                i.Status,
                i.DiscardReason,
                i.ExtractedDataJson
            })
            .ToListAsync(ct);

        return Ok(items);
    }

    /// <summary>
    /// Exporta todos los ítems de una ejecución a Excel (.xlsx).
    /// Incluye las columnas del JSON original (RawData) más los campos normalizados.
    /// </summary>
    [HttpGet("{tenantId:guid}/executions/{executionId:guid}/export")]
    public async Task<IActionResult> ExportExcel(
        Guid tenantId,
        Guid executionId,
        CancellationToken ct = default)
    {
        var executionExists = await db.DelinquencyExecutions
            .AnyAsync(e => e.Id == executionId && e.TenantId == tenantId, ct);
        if (!executionExists) return NotFound();

        var execution = await db.DelinquencyExecutions
            .Where(e => e.Id == executionId)
            .Select(e => new { e.StartedAt, e.TotalItems })
            .FirstAsync(ct);

        // Cargar todos los ítems con su grupo (para nombre del cliente si hace falta)
        var items = await db.DelinquencyItems
            .Where(i => i.ExecutionId == executionId)
            .OrderBy(i => i.RowIndex)
            .Select(i => new
            {
                i.RowIndex,
                i.PhoneRaw,
                i.PhoneNormalized,
                i.ClientName,
                i.PolicyNumber,
                i.Amount,
                i.Status,
                i.DiscardReason,
                i.RawData
            })
            .ToListAsync(ct);

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Descarga");

        // ── Descubrir columnas dinámicas del JSON ─────────────────────────────
        // Usamos el RawData de los primeros 100 ítems para detectar las claves
        var dynamicKeys = new List<string>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items.Where(i => i.RawData != null).Take(200))
        {
            try
            {
                var doc = JsonDocument.Parse(item.RawData!);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (seenKeys.Add(prop.Name))
                        dynamicKeys.Add(prop.Name);
                }
            }
            catch { /* RawData inválido — ignorar */ }
        }

        // ── Cabeceras ─────────────────────────────────────────────────────────
        // Primero: campos normalizados del sistema
        var fixedCols = new[] { "#", "Teléfono (raw)", "Teléfono (normalizado)", "Cliente", "Póliza", "Monto", "Estado", "Razón descarte" };
        // Luego: todas las columnas del JSON original
        var allHeaders = fixedCols.Concat(dynamicKeys).ToList();

        for (int c = 0; c < allHeaders.Count; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = allHeaders[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a5f");
            cell.Style.Font.FontColor = XLColor.White;
        }

        // ── Filas de datos ────────────────────────────────────────────────────
        for (int r = 0; r < items.Count; r++)
        {
            var item = items[r];
            int row = r + 2;

            ws.Cell(row, 1).Value  = item.RowIndex;
            ws.Cell(row, 2).Value  = item.PhoneRaw ?? "";
            ws.Cell(row, 3).Value  = item.PhoneNormalized ?? "";
            ws.Cell(row, 4).Value  = item.ClientName ?? "";
            ws.Cell(row, 5).Value  = item.PolicyNumber ?? "";
            ws.Cell(row, 6).Value  = item.Amount.HasValue ? (double)item.Amount.Value : 0d;
            ws.Cell(row, 7).Value  = item.Status.ToString();
            ws.Cell(row, 8).Value  = item.DiscardReason ?? "";

            // Columnas dinámicas del JSON
            if (item.RawData != null)
            {
                try
                {
                    var doc = JsonDocument.Parse(item.RawData);
                    for (int k = 0; k < dynamicKeys.Count; k++)
                    {
                        if (doc.RootElement.TryGetProperty(dynamicKeys[k], out var val))
                        {
                            var colIdx = fixedCols.Length + k + 1;
                            var cell2 = ws.Cell(row, colIdx);
                            if (val.ValueKind == JsonValueKind.Number && val.TryGetDouble(out var dbl))
                                cell2.Value = dbl;
                            else if (val.ValueKind == JsonValueKind.True)
                                cell2.Value = "Sí";
                            else if (val.ValueKind == JsonValueKind.False)
                                cell2.Value = "No";
                            else if (val.ValueKind == JsonValueKind.Null)
                                cell2.Value = "";
                            else
                                cell2.Value = val.GetString() ?? val.GetRawText();
                        }
                    }
                }
                catch { /* RawData inválido */ }
            }

            // Colorear filas descartadas
            if (item.Status.ToString() == "Discarded")
                ws.Row(row).Style.Fill.BackgroundColor = XLColor.FromHtml("#fff3cd");
        }

        // ── Formato final ─────────────────────────────────────────────────────
        ws.Columns().AdjustToContents(1, 60); // máx 60 chars
        ws.SheetView.FreezeRows(1);

        var stream = new MemoryStream();
        wb.SaveAs(stream);
        stream.Position = 0;

        var dateStr = execution.StartedAt.ToString("yyyyMMdd_HHmm");
        var fileName = $"morosidad_{dateStr}.xlsx";

        return File(stream,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    // ── Maestros de campaña y agentes del tenant (para los selects) ──────────

    [HttpGet("{tenantId:guid}/campaign-templates")]
    public async Task<IActionResult> GetCampaignTemplates(Guid tenantId, CancellationToken ct)
    {
        var templates = await db.CampaignTemplates
            .Where(t => t.TenantId == tenantId && t.IsActive)
            .OrderBy(t => t.Name)
            .Select(t => new { t.Id, t.Name })
            .ToListAsync(ct);

        return Ok(templates);
    }

    [HttpGet("{tenantId:guid}/agents")]
    public async Task<IActionResult> GetAgents(Guid tenantId, CancellationToken ct)
    {
        var agents = await db.AgentDefinitions
            .Where(a => a.TenantId == tenantId && a.IsActive)
            .OrderBy(a => a.Name)
            .Select(a => new { a.Id, a.Name })
            .ToListAsync(ct);

        return Ok(agents);
    }
}

// ── DTOs ──────────────────────────────────────────────────────────────────────

public record AdminUpsertConfigRequest(
    string? CodigoPais,
    string? ItemsJsonPath,
    bool AutoCrearCampanas,
    Guid? CampaignTemplateId,
    Guid? AgentDefinitionId,
    string? CampaignNamePattern,
    string? NotificationEmail,
    string? DownloadWebhookUrl,
    string? DownloadWebhookMethod,
    string? DownloadWebhookHeaders,
    bool IsActive
);

public record AdminSetMappingsRequest(List<AdminMappingItem>? Mappings);

public record AdminMappingItem(
    string ColumnKey,
    string DisplayName,
    string JsonPath,
    string? Role,
    string? RoleLabel,
    string? DataType,
    int? SortOrder,
    string? DefaultValue,
    bool IsEnabled
);
