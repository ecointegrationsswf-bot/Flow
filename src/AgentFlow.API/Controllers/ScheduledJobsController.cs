using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Persistence;
using Cronos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.API.Controllers;

public record ScheduledJobUpsertRequest(
    Guid ActionDefinitionId,
    string TriggerType,        // Cron | EventBased | DelayFromEvent
    string? CronExpression,
    string? TriggerEvent,
    int? DelayMinutes,
    string Scope = "AllTenants",
    bool IsActive = true);

public record CronPreviewRequest(string Expression);

/// <summary>
/// CRUD + ejecución manual + historial de ScheduledWebhookJobs. Pensado para
/// la pantalla admin de Scheduled Jobs en el frontend.
/// </summary>
[ApiController]
[Route("api/scheduled-jobs")]
[Authorize]
public class ScheduledJobsController(
    IScheduledJobRepository jobs,
    IJobExecutionRepository executions,
    AgentFlowDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var list = await jobs.ListAsync(ct);
        return Ok(list.Select(j => new
        {
            j.Id,
            j.ActionDefinitionId,
            ActionName = j.ActionDefinition?.Name,
            j.TriggerType,
            j.CronExpression,
            j.TriggerEvent,
            j.DelayMinutes,
            j.Scope,
            j.IsActive,
            j.NextRunAt,
            j.LastRunAt,
            j.LastRunStatus,
            j.LastRunSummary,
            j.ConsecutiveFailures,
            j.CreatedAt,
            j.UpdatedAt
        }));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var job = await jobs.GetByIdAsync(id, ct);
        return job is null ? NotFound() : Ok(job);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ScheduledJobUpsertRequest req, CancellationToken ct)
    {
        if (!await db.ActionDefinitions.AnyAsync(a => a.Id == req.ActionDefinitionId, ct))
            return BadRequest(new { error = "ActionDefinitionId no existe." });

        var validation = ValidateRequest(req);
        if (validation is not null) return BadRequest(new { error = validation });

        var job = new ScheduledWebhookJob
        {
            Id = Guid.NewGuid(),
            ActionDefinitionId = req.ActionDefinitionId,
            TriggerType = req.TriggerType,
            CronExpression = req.CronExpression,
            TriggerEvent = req.TriggerEvent,
            DelayMinutes = req.DelayMinutes,
            Scope = req.Scope,
            IsActive = req.IsActive,
            NextRunAt = ComputeInitialNextRunAt(req)
        };
        await jobs.AddAsync(job, ct);
        return Ok(new { job.Id });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] ScheduledJobUpsertRequest req, CancellationToken ct)
    {
        var job = await jobs.GetByIdAsync(id, ct);
        if (job is null) return NotFound();

        var validation = ValidateRequest(req);
        if (validation is not null) return BadRequest(new { error = validation });

        job.ActionDefinitionId = req.ActionDefinitionId;
        job.TriggerType = req.TriggerType;
        job.CronExpression = req.CronExpression;
        job.TriggerEvent = req.TriggerEvent;
        job.DelayMinutes = req.DelayMinutes;
        job.Scope = req.Scope;
        job.IsActive = req.IsActive;
        // Recalcular NextRunAt si cambió a Cron
        if (req.TriggerType == "Cron")
            job.NextRunAt = ComputeInitialNextRunAt(req);

        await jobs.UpdateAsync(job, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await jobs.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/run-now")]
    public async Task<IActionResult> RunNow(Guid id, CancellationToken ct)
    {
        var job = await jobs.GetByIdAsync(id, ct);
        if (job is null) return NotFound();

        // Forzamos NextRunAt al pasado para que el siguiente tick lo recoja.
        // Marcamos TriggeredBy="Manual" para distinguirlo en el historial.
        var executionId = await executions.InsertPendingAsync(
            job.Id, DateTime.UtcNow, "Manual", contextId: null, ct);

        await jobs.UpdateAfterRunAsync(
            job.Id, "Pending", "Ejecución manual en cola.",
            DateTime.UtcNow, job.ConsecutiveFailures, ct);

        return Accepted(new { executionId, message = "Job programado para próximo tick." });
    }

    [HttpGet("{id:guid}/executions")]
    public async Task<IActionResult> Executions(Guid id, [FromQuery] int take = 50, CancellationToken ct = default)
    {
        var hist = await executions.GetRecentByJobAsync(id, take, ct);
        return Ok(hist);
    }

    [HttpPost("preview-cron")]
    public IActionResult PreviewCron([FromBody] CronPreviewRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Expression))
            return BadRequest(new { error = "Expresión vacía." });
        try
        {
            var cron = CronExpression.Parse(req.Expression);
            var now = DateTime.UtcNow;
            var next = new List<DateTime>();
            for (int i = 0; i < 5; i++)
            {
                var n = cron.GetNextOccurrence(now, AgentFlow.Infrastructure.ScheduledJobs.PanamaTimeZone.Instance);
                if (n is null) break;
                next.Add(n.Value);
                now = n.Value;
            }
            return Ok(new { valid = true, nextOccurrencesUtc = next });
        }
        catch (Exception ex)
        {
            return Ok(new { valid = false, error = ex.Message });
        }
    }

    private static string? ValidateRequest(ScheduledJobUpsertRequest r)
    {
        var validTriggers = new[] { "Cron", "EventBased", "DelayFromEvent" };
        if (!validTriggers.Contains(r.TriggerType))
            return $"TriggerType debe ser uno de: {string.Join(", ", validTriggers)}.";

        var validScopes = new[] { "AllTenants", "PerCampaign", "PerConversation" };
        if (!validScopes.Contains(r.Scope))
            return $"Scope debe ser uno de: {string.Join(", ", validScopes)}.";

        switch (r.TriggerType)
        {
            case "Cron":
                if (string.IsNullOrWhiteSpace(r.CronExpression))
                    return "CronExpression es obligatorio para TriggerType=Cron.";
                try { CronExpression.Parse(r.CronExpression); }
                catch (Exception ex) { return $"CronExpression inválido: {ex.Message}"; }
                break;
            case "EventBased":
                if (string.IsNullOrWhiteSpace(r.TriggerEvent))
                    return "TriggerEvent es obligatorio para TriggerType=EventBased.";
                break;
            case "DelayFromEvent":
                if (string.IsNullOrWhiteSpace(r.TriggerEvent))
                    return "TriggerEvent es obligatorio para TriggerType=DelayFromEvent.";
                if (r.DelayMinutes is null or < 0)
                    return "DelayMinutes debe ser ≥ 0 para DelayFromEvent.";
                break;
        }
        return null;
    }

    private static DateTime? ComputeInitialNextRunAt(ScheduledJobUpsertRequest r)
    {
        if (r.TriggerType != "Cron") return null;
        try
        {
            var cron = CronExpression.Parse(r.CronExpression!);
            // La cron se interpreta en hora Panamá; Cronos devuelve el próximo run en UTC.
            return cron.GetNextOccurrence(DateTime.UtcNow, AgentFlow.Infrastructure.ScheduledJobs.PanamaTimeZone.Instance);
        }
        catch
        {
            return null;
        }
    }
}
