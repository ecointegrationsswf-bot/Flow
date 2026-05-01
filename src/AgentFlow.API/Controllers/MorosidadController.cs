using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.API.Controllers;

/// <summary>
/// Endpoints del módulo de morosidad:
/// — Configuración de acciones (ActionDelinquencyConfig)
/// — Mapeo de campos (ActionFieldMapping)
/// — Catálogo de campos lógicos (LogicalFieldCatalog)
/// — Historial de ejecuciones + grupos de contacto
/// — Disparador manual para pruebas
/// </summary>
[ApiController]
[Route("api/morosidad")]
[Authorize]
public class MorosidadController(
    AgentFlowDbContext db,
    IDelinquencyProcessor processor) : ControllerBase
{
    // ────────────────────────────────────────────────────────────────────────
    // CATÁLOGO DE CAMPOS LÓGICOS
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>Lista todos los campos lógicos disponibles (catálogo global).</summary>
    [HttpGet("fields")]
    public async Task<IActionResult> GetFieldCatalog(CancellationToken ct)
    {
        var fields = await db.LogicalFieldCatalog
            .Where(f => f.IsActive)
            .OrderBy(f => f.SortOrder)
            .Select(f => new
            {
                f.Id,
                f.Key,
                f.DisplayName,
                f.Description,
                f.DataType,
                f.IsRequired
            })
            .ToListAsync(ct);

        return Ok(fields);
    }

    // ────────────────────────────────────────────────────────────────────────
    // CONFIGURACIÓN POR ACCIÓN (ActionDelinquencyConfig)
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>Obtiene la configuración de morosidad de una acción para el tenant actual.</summary>
    [HttpGet("config/{actionId:guid}")]
    public async Task<IActionResult> GetConfig(Guid actionId, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId == Guid.Empty) return Unauthorized();

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
            config.IsActive
        });
    }

    /// <summary>Crea o actualiza la configuración de morosidad para una acción.</summary>
    [HttpPut("config/{actionId:guid}")]
    public async Task<IActionResult> UpsertConfig(Guid actionId, [FromBody] UpsertConfigRequest req, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId == Guid.Empty) return Unauthorized();

        var existing = await db.ActionDelinquencyConfigs
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.ActionDefinitionId == actionId, ct);

        if (existing == null)
        {
            existing = new ActionDelinquencyConfig
            {
                Id                = Guid.NewGuid(),
                TenantId          = tenantId,
                ActionDefinitionId = actionId,
                CreatedAt         = DateTime.UtcNow
            };
            db.ActionDelinquencyConfigs.Add(existing);
        }

        existing.CodigoPais          = req.CodigoPais ?? "507";
        existing.ItemsJsonPath       = req.ItemsJsonPath;
        existing.AutoCrearCampanas   = req.AutoCrearCampanas;
        existing.CampaignTemplateId  = req.CampaignTemplateId;
        existing.AgentDefinitionId   = req.AgentDefinitionId;
        existing.CampaignNamePattern = req.CampaignNamePattern;
        existing.NotificationEmail   = req.NotificationEmail;
        existing.IsActive            = req.IsActive;
        existing.UpdatedAt           = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return Ok(new { existing.Id });
    }

    // ────────────────────────────────────────────────────────────────────────
    // MAPEO DE CAMPOS (ActionFieldMapping)
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>Obtiene los field mappings de una acción.</summary>
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

    /// <summary>
    /// Reemplaza todos los field mappings de una acción. Valida que existan los 3 roles obligatorios.
    /// </summary>
    [HttpPut("mappings/{actionId:guid}")]
    public async Task<IActionResult> SetMappings(Guid actionId, [FromBody] SetMappingsRequest req, CancellationToken ct)
    {
        var actionExists = await db.ActionDefinitions.AnyAsync(a => a.Id == actionId, ct);
        if (!actionExists) return NotFound("Acción no encontrada.");

        var input = NormalizeColumnKeys(req.Mappings ?? []);
        var validation = ValidateMappings(input);
        if (validation != null) return BadRequest(new { error = validation });

        var existing = await db.ActionFieldMappings
            .Where(m => m.ActionDefinitionId == actionId)
            .ToListAsync(ct);

        db.ActionFieldMappings.RemoveRange(existing);

        var newMappings = input.Select((m, idx) => new ActionFieldMapping
        {
            Id                 = Guid.NewGuid(),
            ActionDefinitionId = actionId,
            ColumnKey          = m.ColumnKey.Trim(),
            DisplayName        = m.DisplayName.Trim(),
            JsonPath           = m.JsonPath.Trim(),
            Role               = ParseRole(m.Role),
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

    private static FieldRole ParseRole(string? role) =>
        Enum.TryParse<FieldRole>(role, ignoreCase: true, out var r) ? r : FieldRole.None;

    /// <summary>
    /// Si una columna no trae ColumnKey, lo deriva del último segmento del JsonPath
    /// (ej: "$.cliente.dueDate" → "dueDate"). Si el resultado choca con otro ColumnKey
    /// del request, le agrega sufijo "_2", "_3", etc.
    /// </summary>
    internal static List<MappingItem> NormalizeColumnKeys(List<MappingItem> mappings)
    {
        if (mappings.Count == 0) return mappings;

        var taken = new HashSet<string>(
            mappings.Where(m => !string.IsNullOrWhiteSpace(m.ColumnKey))
                    .Select(m => m.ColumnKey.Trim()),
            StringComparer.OrdinalIgnoreCase);

        return mappings.Select(m =>
        {
            if (!string.IsNullOrWhiteSpace(m.ColumnKey)) return m;

            var derived = DeriveKeyFromJsonPath(m.JsonPath);
            if (string.IsNullOrEmpty(derived)) derived = "campo";

            var unique = derived;
            var suffix = 2;
            while (!taken.Add(unique))
                unique = $"{derived}_{suffix++}";

            return m with { ColumnKey = unique };
        }).ToList();
    }

    private static string DeriveKeyFromJsonPath(string? jsonPath)
    {
        if (string.IsNullOrWhiteSpace(jsonPath)) return string.Empty;
        var p = jsonPath.Trim().TrimStart('$').TrimStart('.');
        // Quita índices tipo [0] del último segmento: "items[0].telefono" → "telefono"
        var lastSeg = p.Split('.').LastOrDefault() ?? string.Empty;
        var bracket = lastSeg.IndexOf('[');
        if (bracket >= 0) lastSeg = lastSeg[..bracket];
        return new string(lastSeg.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
    }

    /// <summary>
    /// Valida que los mappings tengan los 3 roles obligatorios (Phone, ClientName, KeyValue),
    /// que no haya duplicados de rol no-None, y que KeyValue traiga RoleLabel.
    /// Retorna null si todo OK o el mensaje de error.
    /// </summary>
    internal static string? ValidateMappings(List<MappingItem> mappings)
    {
        if (mappings.Count == 0)
            return "Debe definir al menos los campos obligatorios: Teléfono, Nombre del cliente y KeyValue.";

        // Slugs únicos (ya normalizados aguas arriba) + DisplayName y JsonPath obligatorios
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in mappings)
        {
            var label = !string.IsNullOrWhiteSpace(m.DisplayName) ? m.DisplayName : m.JsonPath;
            if (string.IsNullOrWhiteSpace(m.DisplayName))
                return $"Hay una columna sin nombre visible (JsonPath: '{m.JsonPath}').";
            if (string.IsNullOrWhiteSpace(m.JsonPath))
                return $"La columna '{label}' no tiene JsonPath.";
            if (string.IsNullOrWhiteSpace(m.ColumnKey))
                return $"No se pudo derivar un identificador para la columna '{label}'. Revisa el JsonPath.";
            if (!keys.Add(m.ColumnKey.Trim()))
                return $"Hay dos columnas que apuntan al mismo campo del JSON ('{m.JsonPath}'). Cambia uno o renombra el JsonPath.";
        }

        // Validar roles
        var byRole = mappings
            .Where(m => !string.IsNullOrWhiteSpace(m.Role) && !"None".Equals(m.Role, StringComparison.OrdinalIgnoreCase))
            .GroupBy(m => m.Role!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        foreach (var required in new[] { "Phone", "ClientName", "KeyValue" })
        {
            if (!byRole.ContainsKey(required))
                return $"Falta marcar el campo con rol '{required}'. Sin este rol no se pueden crear campañas ni ligar la gestión al sistema externo.";
        }

        foreach (var (role, count) in byRole)
        {
            if (count > 1)
                return $"El rol '{role}' aparece en {count} columnas. Cada rol semántico sólo puede asignarse a una columna.";
        }

        // KeyValue requiere etiqueta
        var keyValueMapping = mappings.FirstOrDefault(m => "KeyValue".Equals(m.Role, StringComparison.OrdinalIgnoreCase));
        if (keyValueMapping != null && string.IsNullOrWhiteSpace(keyValueMapping.RoleLabel))
            return "El campo con rol 'KeyValue' requiere una etiqueta (ej: 'Número de póliza', 'Cédula').";

        return null;
    }

    // ────────────────────────────────────────────────────────────────────────
    // HISTORIAL DE EJECUCIONES
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>Lista las ejecuciones de morosidad del tenant actual (paginado).</summary>
    [HttpGet("executions")]
    public async Task<IActionResult> ListExecutions(
        [FromQuery] Guid? actionId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken ct = default)
    {
        var tenantId = GetTenantId();
        if (tenantId == Guid.Empty) return Unauthorized();

        var q = db.DelinquencyExecutions.Where(e => e.TenantId == tenantId);

        if (actionId.HasValue)
            q = q.Where(e => e.ActionDefinitionId == actionId.Value);

        if (from.HasValue)
            q = q.Where(e => e.StartedAt >= from.Value.ToUniversalTime());

        if (to.HasValue)
            q = q.Where(e => e.StartedAt <= to.Value.ToUniversalTime().AddDays(1).AddSeconds(-1));

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(e => e.StartedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new
            {
                e.Id,
                e.ActionDefinitionId,
                e.Status,
                e.StartedAt,
                e.CompletedAt,
                e.TotalItems,
                e.ProcessedItems,
                e.DiscardedItems,
                e.GroupsCreated,
                e.CampaignsCreated,
                e.ErrorMessage
            })
            .ToListAsync(ct);

        return Ok(new { total, page, pageSize, items });
    }

    /// <summary>Exporta los ítems de una ejecución a Excel (.xlsx) — portal tenant.</summary>
    [HttpGet("executions/{executionId:guid}/export")]
    public async Task<IActionResult> ExportExcel(Guid executionId, CancellationToken ct = default)
    {
        var tenantId = GetTenantId();
        if (tenantId == Guid.Empty) return Unauthorized();

        var executionExists = await db.DelinquencyExecutions
            .AnyAsync(e => e.Id == executionId && e.TenantId == tenantId, ct);
        if (!executionExists) return NotFound();

        var execution = await db.DelinquencyExecutions
            .Where(e => e.Id == executionId)
            .Select(e => new { e.StartedAt })
            .FirstAsync(ct);

        var items = await db.DelinquencyItems
            .Where(i => i.ExecutionId == executionId)
            .OrderBy(i => i.RowIndex)
            .Select(i => new
            {
                i.RowIndex, i.PhoneRaw, i.PhoneNormalized, i.ClientName,
                i.PolicyNumber, i.Amount, i.Status, i.DiscardReason, i.RawData
            })
            .ToListAsync(ct);

        using var wb = new ClosedXML.Excel.XLWorkbook();
        var ws = wb.Worksheets.Add("Descarga");

        // Columnas dinámicas del JSON
        var dynamicKeys = new List<string>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items.Where(i => i.RawData != null).Take(200))
        {
            try
            {
                var doc = System.Text.Json.JsonDocument.Parse(item.RawData!);
                foreach (var prop in doc.RootElement.EnumerateObject())
                    if (seenKeys.Add(prop.Name)) dynamicKeys.Add(prop.Name);
            }
            catch { }
        }

        var fixedCols = new[] { "#", "Teléfono (raw)", "Teléfono (normalizado)", "Cliente", "Póliza", "Monto", "Estado", "Razón descarte" };
        var allHeaders = fixedCols.Concat(dynamicKeys).ToList();

        for (int c = 0; c < allHeaders.Count; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = allHeaders[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#1e3a5f");
            cell.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
        }

        for (int r = 0; r < items.Count; r++)
        {
            var item = items[r]; int row = r + 2;
            ws.Cell(row, 1).Value = item.RowIndex;
            ws.Cell(row, 2).Value = item.PhoneRaw ?? "";
            ws.Cell(row, 3).Value = item.PhoneNormalized ?? "";
            ws.Cell(row, 4).Value = item.ClientName ?? "";
            ws.Cell(row, 5).Value = item.PolicyNumber ?? "";
            ws.Cell(row, 6).Value = item.Amount.HasValue ? (double)item.Amount.Value : 0d;
            ws.Cell(row, 7).Value = item.Status.ToString();
            ws.Cell(row, 8).Value = item.DiscardReason ?? "";

            if (item.RawData != null)
            {
                try
                {
                    var doc = System.Text.Json.JsonDocument.Parse(item.RawData);
                    for (int k = 0; k < dynamicKeys.Count; k++)
                    {
                        if (doc.RootElement.TryGetProperty(dynamicKeys[k], out var val))
                        {
                            var cell2 = ws.Cell(row, fixedCols.Length + k + 1);
                            if (val.ValueKind == System.Text.Json.JsonValueKind.Number && val.TryGetDouble(out var dbl))
                                cell2.Value = dbl;
                            else if (val.ValueKind == System.Text.Json.JsonValueKind.Null)
                                cell2.Value = "";
                            else if (val.ValueKind == System.Text.Json.JsonValueKind.True)
                                cell2.Value = "Sí";
                            else if (val.ValueKind == System.Text.Json.JsonValueKind.False)
                                cell2.Value = "No";
                            else
                                cell2.Value = val.GetString() ?? val.GetRawText();
                        }
                    }
                }
                catch { }
            }
            if (item.Status.ToString() == "Discarded")
                ws.Row(row).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#fff3cd");
        }

        ws.Columns().AdjustToContents(1, 60);
        ws.SheetView.FreezeRows(1);

        var stream = new MemoryStream();
        wb.SaveAs(stream);
        stream.Position = 0;
        return File(stream,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"descarga_{execution.StartedAt:yyyyMMdd_HHmm}.xlsx");
    }

    /// <summary>Obtiene el detalle de una ejecución.</summary>
    [HttpGet("executions/{executionId:guid}")]
    public async Task<IActionResult> GetExecution(Guid executionId, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId == Guid.Empty) return Unauthorized();

        var execution = await db.DelinquencyExecutions
            .Where(e => e.TenantId == tenantId && e.Id == executionId)
            .Select(e => new
            {
                e.Id,
                e.ActionDefinitionId,
                e.Status,
                e.StartedAt,
                e.CompletedAt,
                e.TotalItems,
                e.ProcessedItems,
                e.DiscardedItems,
                e.GroupsCreated,
                e.CampaignsCreated,
                e.ErrorMessage
            })
            .FirstOrDefaultAsync(ct);

        if (execution == null) return NotFound();
        return Ok(execution);
    }

    /// <summary>Lista los grupos de contacto de una ejecución.</summary>
    [HttpGet("executions/{executionId:guid}/groups")]
    public async Task<IActionResult> GetGroups(
        Guid executionId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var tenantId = GetTenantId();
        if (tenantId == Guid.Empty) return Unauthorized();

        // Verificar que la ejecución pertenece al tenant
        var executionExists = await db.DelinquencyExecutions
            .AnyAsync(e => e.Id == executionId && e.TenantId == tenantId, ct);
        if (!executionExists) return NotFound();

        var total = await db.ContactGroups.CountAsync(g => g.ExecutionId == executionId, ct);

        var groups = await db.ContactGroups
            .Where(g => g.ExecutionId == executionId)
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

    /// <summary>Lista los ítems (pólizas) de un grupo de contacto.</summary>
    [HttpGet("executions/{executionId:guid}/groups/{groupId:guid}/items")]
    public async Task<IActionResult> GetGroupItems(
        Guid executionId,
        Guid groupId,
        CancellationToken ct = default)
    {
        var tenantId = GetTenantId();
        if (tenantId == Guid.Empty) return Unauthorized();

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

    /// <summary>Lista los ítems descartados de una ejecución.</summary>
    [HttpGet("executions/{executionId:guid}/discarded")]
    public async Task<IActionResult> GetDiscarded(Guid executionId, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId == Guid.Empty) return Unauthorized();

        var executionExists = await db.DelinquencyExecutions
            .AnyAsync(e => e.Id == executionId && e.TenantId == tenantId, ct);
        if (!executionExists) return NotFound();

        var items = await db.DelinquencyItems
            .Where(i => i.ExecutionId == executionId && i.Status == Domain.Enums.DelinquencyItemStatus.Discarded)
            .OrderBy(i => i.RowIndex)
            .Select(i => new
            {
                i.Id,
                i.RowIndex,
                i.PhoneRaw,
                i.ClientName,
                i.DiscardReason
            })
            .ToListAsync(ct);

        return Ok(items);
    }

    // ────────────────────────────────────────────────────────────────────────
    // DISPARADOR MANUAL (para pruebas / integración)
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Procesa manualmente un payload JSON de morosidad para una acción.
    /// Útil para pruebas y para integración con n8n/webhooks externos.
    /// </summary>
    [HttpPost("process/{actionId:guid}")]
    public async Task<IActionResult> ProcessManual(Guid actionId, [FromBody] ProcessManualRequest req, CancellationToken ct)
    {
        var tenantId = GetTenantId();
        if (tenantId == Guid.Empty) return Unauthorized();

        if (string.IsNullOrWhiteSpace(req.JsonPayload))
            return BadRequest("jsonPayload es requerido.");

        var executionId = await processor.ProcessAsync(tenantId, actionId, req.JsonPayload, null, ct);
        return Ok(new { executionId });
    }

    // ────────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────────

    private Guid GetTenantId()
    {
        var claim = User.Claims.FirstOrDefault(c => c.Type == "tenant_id" || c.Type == "TenantId");
        return claim != null && Guid.TryParse(claim.Value, out var id) ? id : Guid.Empty;
    }
}

// ── DTOs ─────────────────────────────────────────────────────────────────────

public record UpsertConfigRequest(
    string? CodigoPais,
    string? ItemsJsonPath,
    bool AutoCrearCampanas,
    Guid? CampaignTemplateId,
    Guid? AgentDefinitionId,
    string? CampaignNamePattern,
    string? NotificationEmail,
    bool IsActive
);

public record SetMappingsRequest(List<MappingItem>? Mappings);

public record MappingItem(
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

public record ProcessManualRequest(string JsonPayload);
