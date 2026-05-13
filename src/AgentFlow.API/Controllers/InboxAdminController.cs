using AgentFlow.Domain.Entities;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.API.Controllers;

/// <summary>
/// Endpoints de monitoreo de la cola InboundMessageQueue.
/// SOLO super admin — los corredores (tenants) NO ven esta información
/// porque expone detalle interno cross-tenant.
/// </summary>
[ApiController]
[Route("api/admin/inbox")]
[Authorize(Roles = "super_admin")]
public class InboxAdminController(AgentFlowDbContext db) : ControllerBase
{
    /// <summary>
    /// Resumen agregado: conteo por status en las últimas N horas,
    /// item más viejo en Pending/Failed, edad del peor caso.
    /// </summary>
    [HttpGet("summary")]
    public async Task<IActionResult> Summary([FromQuery] int hours = 24, CancellationToken ct = default)
    {
        var since = DateTime.UtcNow.AddHours(-Math.Clamp(hours, 1, 24 * 30));

        var byStatus = await db.InboundMessageQueueItems
            .Where(x => x.FirstReceivedAt >= since)
            .GroupBy(x => x.Status)
            .Select(g => new { status = g.Key, count = g.Count() })
            .ToListAsync(ct);

        var stuckCount = await db.InboundMessageQueueItems
            .CountAsync(x => x.Status != InboundMessageStatus.Replied
                          && x.Status != InboundMessageStatus.Escalated
                          && x.FirstReceivedAt < DateTime.UtcNow.AddSeconds(-120), ct);

        var oldestUnresolved = await (
            from x in db.InboundMessageQueueItems
            where x.Status != InboundMessageStatus.Replied
               && x.Status != InboundMessageStatus.Escalated
            join t in db.Tenants on x.TenantId equals t.Id into tg
            from t in tg.DefaultIfEmpty()
            orderby x.FirstReceivedAt
            select new
            {
                x.Id,
                x.Status,
                x.FirstReceivedAt,
                x.TenantId,
                tenantName = t != null ? t.Name : null,
                x.FromPhone,
            }).FirstOrDefaultAsync(ct);

        var failedAttempts = await db.InboundMessageQueueItems
            .Where(x => x.Status == InboundMessageStatus.Failed && x.FirstReceivedAt >= since)
            .Select(x => new { x.Id, x.AttemptCount, x.LastErrorStep, x.LastError })
            .Take(50)
            .ToListAsync(ct);

        return Ok(new
        {
            sinceUtc = since,
            byStatus,
            stuckCount,
            oldestUnresolved,
            failedSamples = failedAttempts,
        });
    }

    /// <summary>
    /// Listado paginado con filtros. Sin info sensible del cliente más allá
    /// del teléfono que ya está en el resto de la BD.
    ///
    /// Default — últimas 24h. La tabla crecerá rápido en producción: un tenant
    /// con 500 mensajes/día genera ~180k filas/año. El default acota la vista
    /// y la paginación + filtros mantienen el query barato.
    /// </summary>
    [HttpGet("items")]
    public async Task<IActionResult> Items(
        [FromQuery] string? status = null,
        [FromQuery] Guid? tenantId = null,
        [FromQuery] string? phone = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int take = 50,
        [FromQuery] int skip = 0,
        CancellationToken ct = default)
    {
        // Default 24h si el cliente no manda rango. Acota el query y reduce el
        // costo del COUNT cuando la tabla tiene millones de filas.
        var fromUtc = from?.ToUniversalTime() ?? DateTime.UtcNow.AddHours(-24);
        var toUtc   = to?.ToUniversalTime()   ?? DateTime.UtcNow;

        var q = db.InboundMessageQueueItems.AsQueryable()
            .Where(x => x.FirstReceivedAt >= fromUtc && x.FirstReceivedAt <= toUtc);

        if (!string.IsNullOrEmpty(status))
            q = q.Where(x => x.Status == status);
        if (tenantId.HasValue)
            q = q.Where(x => x.TenantId == tenantId.Value);
        if (!string.IsNullOrEmpty(phone))
        {
            var normalized = phone.Trim();
            q = q.Where(x => x.FromPhone.Contains(normalized));
        }

        var total = await q.CountAsync(ct);

        // Paginación primero (sobre la tabla principal indexada), JOIN después
        // sobre el subset reducido. Patrón habitual para listados grandes.
        var clampedTake = Math.Clamp(take, 1, 200);
        var clampedSkip = Math.Max(skip, 0);
        var pagedIds = await q
            .OrderByDescending(x => x.FirstReceivedAt)
            .Skip(clampedSkip).Take(clampedTake)
            .Select(x => x.Id)
            .ToListAsync(ct);

        var items = await (
            from x in db.InboundMessageQueueItems.Where(i => pagedIds.Contains(i.Id))
            join t in db.Tenants on x.TenantId equals t.Id into tg
            from t in tg.DefaultIfEmpty()
            orderby x.FirstReceivedAt descending
            select new
            {
                x.Id,
                x.TenantId,
                tenantName = t != null ? t.Name : null,
                x.FromPhone,
                x.Channel,
                x.Status,
                x.FirstReceivedAt,
                x.LastReceivedAt,
                x.CompletedAt,
                x.AttemptCount,
                x.LastErrorStep,
                x.LastError,
                x.ClaimedBy,
                ageSec = (int)(DateTime.UtcNow - x.FirstReceivedAt).TotalSeconds,
            }).ToListAsync(ct);

        return Ok(new { total, take = clampedTake, skip = clampedSkip, fromUtc, toUtc, items });
    }

    /// <summary>
    /// Catálogo ligero de tenants — para llenar el dropdown del filtro.
    /// Solo Id + Name, sin más detalle.
    /// </summary>
    [HttpGet("tenants-lite")]
    public async Task<IActionResult> TenantsLite(CancellationToken ct)
    {
        var tenants = await db.Tenants
            .OrderBy(t => t.Name)
            .Select(t => new { id = t.Id, name = t.Name })
            .ToListAsync(ct);
        return Ok(tenants);
    }

    /// <summary>
    /// Re-encola un item en Pending para forzar reintento por el dispatcher.
    /// Útil cuando el operador identifica que la causa transitoria ya pasó
    /// (ej: rotaron una credencial, Anthropic se recuperó, etc.).
    /// Limpia AttemptCount para darle ciclos completos de retry.
    /// </summary>
    [HttpPost("{id:guid}/retry")]
    public async Task<IActionResult> Retry(Guid id, CancellationToken ct)
    {
        var rows = await db.Database.ExecuteSqlRawAsync(@"
            UPDATE InboundMessageQueueItems
            SET Status = 'Pending',
                AttemptCount = 0,
                ClaimedAt = NULL,
                ClaimedBy = NULL,
                StartedAt = NULL,
                CompletedAt = NULL,
                LastReceivedAt = SYSUTCDATETIME()
            WHERE Id = {0}
              AND Status IN ('Failed','Escalated','Claimed');", new object[] { id }, ct);

        if (rows == 0)
            return NotFound(new { error = "Item no encontrado o no es elegible para retry." });

        return Ok(new { ok = true, requeued = id });
    }
}
