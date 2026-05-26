using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.API.Controllers;

/// <summary>
/// CRUD + listado de la lista negra de números sin WhatsApp.
///
/// Modelo de permisos:
/// - SuperAdmin ve y opera sobre TODOS los tenants (incluye cross-tenant TenantId=NULL).
/// - Admin/Supervisor del tenant ve solo entradas que lo afectan (TenantId del tenant
///   o cross-tenant). NO puede borrar entradas cross-tenant (solo sus propias entradas).
/// </summary>
[ApiController]
[Authorize]
[Route("api/invalid-numbers")]
public class InvalidNumbersController(
    AgentFlowDbContext db,
    ITenantContext tenantCtx,
    IWhatsAppNumberValidator validator) : ControllerBase
{
    /// <summary>Listado paginado + filtros para la vista de admin.</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? q = null,
        [FromQuery] string? source = null,
        [FromQuery] bool? isActive = true,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var tenantId = tenantCtx.TenantId;
        var isSuperAdmin = tenantId == Guid.Empty;

        var query = db.InvalidWhatsAppNumbers.AsNoTracking().AsQueryable();

        // Visibilidad: super admin ve todo. Tenant ve sus entradas + las cross-tenant.
        if (!isSuperAdmin)
            query = query.Where(x => x.TenantId == tenantId || x.TenantId == null);

        if (isActive.HasValue)
            query = query.Where(x => x.IsActive == isActive.Value);

        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(x => x.PhoneNumber.Contains(q) || x.Reason.Contains(q));

        if (!string.IsNullOrWhiteSpace(source))
            query = query.Where(x => x.Source == source);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(x => x.LastCheckedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new
            {
                x.Id,
                x.PhoneNumber,
                x.Reason,
                x.Source,
                x.FirstDetectedAt,
                x.LastCheckedAt,
                x.OccurrenceCount,
                x.TenantId,
                x.LastTenantId,
                x.LastCampaignId,
                x.Notes,
                x.IsActive,
                Scope = x.TenantId.HasValue ? "tenant" : "global"
            })
            .ToListAsync(ct);

        return Ok(new { total, page, pageSize, items });
    }

    /// <summary>Restaurar (soft delete) un número de la lista negra.</summary>
    [HttpPost("{id:guid}/restore")]
    public async Task<IActionResult> Restore(Guid id, CancellationToken ct)
    {
        var userId = ResolveUserId();
        var ok = await validator.RestoreAsync(id, userId, ct);
        if (!ok) return NotFound(new { error = "No encontrado o ya estaba restaurado." });
        return Ok(new { restored = true });
    }

    /// <summary>Agregar manualmente un número a la lista negra.</summary>
    [HttpPost]
    public async Task<IActionResult> AddManual([FromBody] AddInvalidNumberDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.PhoneNumber))
            return BadRequest(new { error = "PhoneNumber requerido." });

        var tenantId = tenantCtx.TenantId == Guid.Empty ? (Guid?)null : tenantCtx.TenantId;

        await validator.RegisterAsBlacklistedAsync(
            phoneNumber: dto.PhoneNumber.Trim(),
            reason: string.IsNullOrWhiteSpace(dto.Reason) ? "Registrado manualmente" : dto.Reason.Trim(),
            source: "manual",
            tenantId: dto.IsGlobal == true ? null : tenantId,
            campaignId: null,
            userId: ResolveUserId(),
            ct: ct);

        return Ok(new { added = true });
    }

    private Guid ResolveUserId()
    {
        var sub = User.FindFirst("sub")?.Value ?? User.FindFirst("nameid")?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }

    public record AddInvalidNumberDto(string PhoneNumber, string? Reason, bool? IsGlobal);
}
