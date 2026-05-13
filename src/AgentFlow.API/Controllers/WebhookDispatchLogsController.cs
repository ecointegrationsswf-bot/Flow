using AgentFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.API.Controllers;

/// <summary>
/// Auditoría per-call de los dispatches HTTP que salen del sistema vía
/// IActionExecutorService. Permite ver el JSON exacto enviado, la respuesta del
/// endpoint remoto, y trazar el call hacia su origen (tenant, conversación,
/// scheduled job execution, action slug).
/// </summary>
[ApiController]
[Route("api/webhook-dispatch-logs")]
[Authorize]
public class WebhookDispatchLogsController(AgentFlowDbContext db) : ControllerBase
{
    /// <summary>
    /// Lista logs filtrados. Devuelve los 200 más recientes por defecto.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] Guid? tenantId,
        [FromQuery] Guid? conversationId,
        [FromQuery] Guid? jobExecutionId,
        [FromQuery] Guid? jobId,
        [FromQuery] string? clientPhone,
        [FromQuery] string? actionSlug,
        [FromQuery] string? status,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int take = 200,
        CancellationToken ct = default)
    {
        if (take is < 1 or > 1000) take = 200;

        var q = db.WebhookDispatchLogs.AsNoTracking().AsQueryable();

        if (tenantId.HasValue)        q = q.Where(l => l.TenantId == tenantId);
        if (conversationId.HasValue)  q = q.Where(l => l.ConversationId == conversationId);
        if (jobExecutionId.HasValue)  q = q.Where(l => l.JobExecutionId == jobExecutionId);
        if (jobId.HasValue)           q = q.Where(l => l.JobId == jobId);
        if (!string.IsNullOrEmpty(clientPhone)) q = q.Where(l => l.ClientPhone == clientPhone);
        if (!string.IsNullOrEmpty(actionSlug))  q = q.Where(l => l.ActionSlug == actionSlug);
        if (!string.IsNullOrEmpty(status))      q = q.Where(l => l.Status == status);
        if (from.HasValue)            q = q.Where(l => l.StartedAt >= from);
        if (to.HasValue)              q = q.Where(l => l.StartedAt < to);

        var logs = await q
            .OrderByDescending(l => l.StartedAt)
            .Take(take)
            .Select(l => new
            {
                l.Id,
                l.TenantId,
                l.ConversationId,
                l.ClientPhone,
                l.JobExecutionId,
                l.JobId,
                l.ActionSlug,
                l.TargetUrl,
                l.HttpMethod,
                l.RequestContentType,
                l.RequestPayloadJson,
                l.ResponseStatusCode,
                l.ResponseBody,
                l.DurationMs,
                l.Status,
                l.ErrorMessage,
                l.StartedAt,
                l.CompletedAt
            })
            .ToListAsync(ct);

        return Ok(logs);
    }

    /// <summary>Detalle individual.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var log = await db.WebhookDispatchLogs
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == id, ct);
        return log is null ? NotFound() : Ok(log);
    }

    /// <summary>
    /// Resumen agrupado por status y action slug — útil para verificar de un
    /// vistazo cuántos calls hubo y cuántos fueron a cada URL en una ventana.
    /// </summary>
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(
        [FromQuery] Guid? tenantId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct = default)
    {
        var q = db.WebhookDispatchLogs.AsNoTracking().AsQueryable();
        if (tenantId.HasValue) q = q.Where(l => l.TenantId == tenantId);
        if (from.HasValue)     q = q.Where(l => l.StartedAt >= from);
        if (to.HasValue)       q = q.Where(l => l.StartedAt < to);

        var rows = await q
            .GroupBy(l => new { l.ActionSlug, l.Status, l.TargetUrl, l.ResponseStatusCode })
            .Select(g => new
            {
                g.Key.ActionSlug,
                g.Key.Status,
                g.Key.TargetUrl,
                g.Key.ResponseStatusCode,
                Count = g.Count(),
                AvgDurationMs = g.Average(l => l.DurationMs),
                LastAt = g.Max(l => l.StartedAt)
            })
            .OrderByDescending(r => r.LastAt)
            .ToListAsync(ct);

        return Ok(rows);
    }
}
