using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.ScheduledJobs;

/// <summary>
/// Implementación del IWebhookEventDispatcher. Cuando alguien anuncia un evento:
///   1. Busca ScheduledWebhookJobs con TriggerEvent matcheante e IsActive=true.
///   2. Para cada job calcula NextRunAt según TriggerType:
///        EventBased     → NextRunAt = now (lo recogerá el siguiente tick).
///        DelayFromEvent → NextRunAt = now + DelayMinutes.
///        Cron           → ignorado (el cron tiene su propio scheduler).
///   3. Aplica circuit breaker por (tenantId:actionSlug) — si la combinación
///      acumuló N fallos en ventana de 5 min, no programa nuevos jobs hasta que
///      el contador caduque. Evita amplificar caídas de un endpoint del cliente.
/// </summary>
public class WebhookEventDispatcher(
    AgentFlowDbContext db,
    IMemoryCache cache,
    ILogger<WebhookEventDispatcher> log) : IWebhookEventDispatcher
{
    // Ventana del circuit breaker por (tenant:slug). Coordinada con el threshold
    // del Worker (5 fallos → pause), pero acá actuamos como gate adicional para
    // evitar incluso programar jobs cuando el endpoint está caído.
    private static readonly TimeSpan BreakerWindow = TimeSpan.FromMinutes(5);
    private const int BreakerFailureThreshold = 5;

    public async Task DispatchAsync(
        string eventName,
        string? contextId,
        Guid tenantId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(eventName)) return;

        // Cargamos los jobs que deben reaccionar al evento. Include de ActionDefinition
        // para conocer el slug en el log y para futura evaluación del circuit breaker.
        var jobs = await db.ScheduledWebhookJobs
            .Include(j => j.ActionDefinition)
            .Where(j => j.IsActive
                        && j.TriggerEvent == eventName
                        && (j.TriggerType == "EventBased" || j.TriggerType == "DelayFromEvent"))
            .ToListAsync(ct);

        if (jobs.Count == 0)
        {
            // Silencioso: no es error que un evento no tenga jobs registrados.
            return;
        }

        var now = DateTime.UtcNow;
        int scheduled = 0, breakered = 0;
        foreach (var job in jobs)
        {
            var slug = job.ActionDefinition?.Name ?? "unknown";
            var breakerKey = $"breaker:{tenantId:N}:{slug}";

            // Si el breaker está abierto, no programamos. El admin verá ConsecutiveFailures
            // creciente en la UI y podrá actuar.
            if (cache.TryGetValue<int>(breakerKey, out var failures) && failures >= BreakerFailureThreshold)
            {
                breakered++;
                log.LogWarning(
                    "[Dispatch] Breaker abierto para {Tenant}:{Slug} ({Failures} fallos). Job {JobId} no programado.",
                    tenantId, slug, failures, job.Id);
                continue;
            }

            var nextRunAt = job.TriggerType == "DelayFromEvent" && job.DelayMinutes.HasValue
                ? now.AddMinutes(job.DelayMinutes.Value)
                : now;

            // Solo actualizamos si el nuevo NextRunAt es más cercano que el actual,
            // o si no hay uno programado. Evita mover hacia atrás un job ya planeado.
            await db.ScheduledWebhookJobs
                .Where(j => j.Id == job.Id
                            && (j.NextRunAt == null || j.NextRunAt > nextRunAt))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.NextRunAt, nextRunAt)
                    .SetProperty(j => j.UpdatedAt, DateTime.UtcNow), ct);

            scheduled++;
        }

        log.LogInformation(
            "[Dispatch] event={Event} tenant={Tenant} ctx={Ctx} → {Scheduled} jobs programados ({Breakered} bloqueados por breaker).",
            eventName, tenantId, contextId, scheduled, breakered);
    }

    /// <summary>
    /// API de uso interno: el Worker la llama tras una falla para incrementar el
    /// contador del breaker. No se expone en la interfaz porque solo el Worker
    /// debe tocar este estado.
    /// </summary>
    public void RegisterFailure(Guid tenantId, string actionSlug)
    {
        var key = $"breaker:{tenantId:N}:{actionSlug}";
        var current = cache.TryGetValue<int>(key, out var v) ? v : 0;
        cache.Set(key, current + 1, BreakerWindow);
    }

    /// <summary>Resetea el breaker tras un éxito.</summary>
    public void RegisterSuccess(Guid tenantId, string actionSlug)
    {
        cache.Remove($"breaker:{tenantId:N}:{actionSlug}");
    }
}
