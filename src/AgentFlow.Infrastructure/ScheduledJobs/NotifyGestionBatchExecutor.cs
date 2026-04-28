using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Domain.Webhooks;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.ScheduledJobs;

/// <summary>
/// Executor batch del slug NOTIFY_GESTION. Diseñado para correr en cron a una
/// hora fija (ej: 22:00 Panamá), una vez por día. En cada corrida itera todas
/// las conversaciones etiquetadas con resultJson disponible que hayan sido
/// (re)etiquetadas DESPUÉS del último run del job, y dispara un webhook por
/// cada una usando el InputSchema/OutputSchema del ActionDefinition global
/// NOTIFY_GESTION (configurado por el admin en el Webhook Builder).
///
/// Idempotencia: filtramos por <c>Conversation.LabeledAt &gt; job.LastRunAt</c>
/// — si el cron corre cada día y ninguna conv fue re-etiquetada, no se reenvía
/// nada. Si una conv fue re-etiquetada (LastActivityAt &gt; LabeledAt fue
/// detectado por el LabelingJob y refrescó LabeledAt) la siguiente corrida
/// vuelve a enviar.
///
/// Scope esperado: AllTenants. El executor itera todos los tenants y solo
/// procesa los que tengan conversaciones candidatas.
/// </summary>
public class NotifyGestionBatchExecutor(
    AgentFlowDbContext db,
    IActionExecutorService actionExecutor,
    ILogger<NotifyGestionBatchExecutor> log) : IScheduledJobExecutor
{
    public string Slug => "NOTIFY_GESTION";

    public async Task<JobRunResult> ExecuteAsync(
        ScheduledWebhookJob job, ScheduledJobContext ctx, CancellationToken ct)
    {
        // Cutoff: solo procesar conversaciones etiquetadas/re-etiquetadas tras la última corrida
        // exitosa del job. Si nunca corrió, procesamos las labeladas en los últimos 30 días.
        var cutoff = job.LastRunAt ?? DateTime.UtcNow.AddDays(-30);

        // Idempotencia: descartamos conversaciones que ya tengan al menos un
        // WebhookDispatchLog de NOTIFY_GESTION exitoso DESPUÉS de su LabeledAt.
        // Si la conversación se re-etiqueta (LabeledAt avanza), los Success viejos
        // quedan "obsoletos" (StartedAt < nuevo LabeledAt) y se permite el reenvío
        // con el contenido actualizado. Los fallos (HTTP 5xx) no cuentan, así
        // los reintentos siguen funcionando.
        var pending = await db.Conversations
            .AsNoTracking()
            .Where(c => c.LabelId != null
                        && c.LabelingResultJson != null
                        && c.LabeledAt != null
                        && c.LabeledAt > cutoff
                        && !db.WebhookDispatchLogs.Any(l =>
                                l.ConversationId == c.Id
                                && l.ActionSlug == "NOTIFY_GESTION"
                                && l.Status == "Success"
                                && l.StartedAt >= c.LabeledAt))
            .Select(c => new
            {
                c.Id,
                c.TenantId,
                c.ClientPhone,
                c.CampaignId,
            })
            .ToListAsync(ct);

        if (pending.Count == 0)
            return JobRunResult.Skipped($"Sin conversaciones etiquetadas tras {cutoff:O}.");

        log.LogInformation("NotifyGestionBatch: {N} conversaciones candidatas (cutoff={Cutoff:O}).",
            pending.Count, cutoff);

        // Resolver CampaignTemplateId en una sola query (necesario para el ActionExecutor).
        var campaignIds = pending.Where(p => p.CampaignId.HasValue).Select(p => p.CampaignId!.Value).Distinct().ToList();
        var templateByCampaign = await db.Campaigns
            .Where(c => campaignIds.Contains(c.Id))
            .Select(c => new { c.Id, c.CampaignTemplateId })
            .ToDictionaryAsync(c => c.Id, c => c.CampaignTemplateId, ct);

        var sent = 0;
        var failed = 0;
        var errorMessages = new List<string>();

        foreach (var p in pending)
        {
            if (ct.IsCancellationRequested) break;

            var campaignTemplateId = p.CampaignId.HasValue && templateByCampaign.TryGetValue(p.CampaignId.Value, out var tplId)
                ? (Guid?)tplId
                : null;

            try
            {
                var result = await actionExecutor.ExecuteAsync(
                    actionSlug: Slug,
                    tenantId: p.TenantId,
                    campaignTemplateId: campaignTemplateId,
                    contactPhone: p.ClientPhone,
                    conversationId: p.Id,
                    collectedParams: new CollectedParams(),
                    agentSlug: null,
                    jobExecutionId: ctx.ExecutionId,
                    jobId: job.Id,
                    ct: ct);

                if (result.Success) sent++;
                else
                {
                    failed++;
                    var msg = $"conv {p.Id}: {result.ErrorMessage ?? "sin detalle"}";
                    if (errorMessages.Count < 5) errorMessages.Add(msg);
                    log.LogWarning("NotifyGestionBatch falló para conv {Conv}: {Err}", p.Id, result.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                failed++;
                if (errorMessages.Count < 5) errorMessages.Add($"conv {p.Id}: {ex.Message}");
                log.LogError(ex, "NotifyGestionBatch: excepción procesando conv {Conv}.", p.Id);
            }
        }

        var summary = $"Total={pending.Count} · Enviadas={sent} · Fallos={failed}";
        if (errorMessages.Count > 0) summary += " · Errores: " + string.Join(" | ", errorMessages);
        if (summary.Length > 800) summary = summary[..800];

        log.LogInformation("NotifyGestionBatch completo: {Summary}", summary);

        if (sent == 0 && failed == 0) return JobRunResult.Skipped(summary);
        if (failed == 0) return JobRunResult.Success(sent, summary);
        if (sent == 0) return JobRunResult.Failed("Todos los envíos fallaron.", summary);
        return JobRunResult.Partial(pending.Count, sent, failed, summary);
    }
}
