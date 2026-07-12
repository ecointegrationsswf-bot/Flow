using System.Text.Json;
using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Domain.Webhooks;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.ScheduledJobs;

/// <summary>
/// Integración Ludo CRM — Fase 4 (degradación suave). Drena LudoOutboxItems Pending:
/// reintenta las llamadas de salida a Ludo (registrar_oportunidad / mover_fase /
/// registrar_nota) que fallaron durante una conversación.
///
/// Re-ejecuta vía <see cref="IActionExecutorService"/> con los params ORIGINALES del
/// agente — así el enricher re-resuelve faseId y {oportunidadId} con datos frescos, y
/// como el API de Ludo es idempotente (upsert por teléfono, mover-fase no duplica
/// movimientos), reintentar es seguro.
///
/// Backoff exponencial: 2^intentos minutos (cap 60). Tras <see cref="MaxAttempts"/>
/// intentos el item pasa a Failed (queda auditable, no se martilla más).
///
/// Slug: <c>LUDO_OUTBOX_DRAINER</c> — cron global cada 5 min (LudoActionSeeder).
/// </summary>
public class LudoOutboxDrainerExecutor(
    AgentFlowDbContext db,
    IActionExecutorService actionExecutor,
    ILogger<LudoOutboxDrainerExecutor> log) : IScheduledJobExecutor
{
    public string Slug => "LUDO_OUTBOX_DRAINER";

    private const int MaxPerTick = 25;
    private const int MaxAttempts = 8;

    public async Task<JobRunResult> ExecuteAsync(ScheduledWebhookJob job, ScheduledJobContext ctx, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var due = await db.LudoOutboxItems
            .Where(o => o.Status == "Pending"
                     && (o.NextAttemptAt == null || o.NextAttemptAt <= now))
            .OrderBy(o => o.CreatedAt)
            .Take(MaxPerTick)
            .ToListAsync(ct);

        if (due.Count == 0)
            return JobRunResult.Skipped("Sin items Pending en el LudoOutbox.");

        var ok = 0;
        var failed = 0;
        foreach (var item in due)
        {
            if (ct.IsCancellationRequested) break;

            item.Attempts++;
            item.UpdatedAt = DateTime.UtcNow;
            try
            {
                var values = JsonSerializer.Deserialize<Dictionary<string, string?>>(item.PayloadJson)
                             ?? new Dictionary<string, string?>();
                var result = await actionExecutor.ExecuteAsync(
                    item.ActionSlug,
                    item.TenantId,
                    campaignTemplateId: null,
                    contactPhone: item.PhoneE164,
                    conversationId: item.ConversationId,
                    collectedParams: new CollectedParams { Values = values },
                    ct: ct);

                if (result.Success)
                {
                    item.Status = "Sent";
                    item.LastError = null;
                    item.NextAttemptAt = null;
                    ok++;
                    continue;
                }

                item.LastError = result.ErrorMessage ?? "Reintento fallido.";
            }
            catch (Exception ex)
            {
                item.LastError = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
                log.LogWarning(ex, "[LudoDrainer] Reintento con excepción item={ItemId} slug={Slug}",
                    item.Id, item.ActionSlug);
            }

            // Fallo — backoff exponencial o abandono definitivo.
            failed++;
            if (item.Attempts >= MaxAttempts)
            {
                item.Status = "Failed";
                item.NextAttemptAt = null;
                log.LogWarning("[LudoDrainer] Item {ItemId} ({Slug}, tenant={TenantId}) agotó {Max} intentos — Failed.",
                    item.Id, item.ActionSlug, item.TenantId, MaxAttempts);
            }
            else
            {
                var backoffMinutes = Math.Min(60, Math.Pow(2, item.Attempts));
                item.NextAttemptAt = DateTime.UtcNow.AddMinutes(backoffMinutes);
            }
        }

        await db.SaveChangesAsync(ct);

        var summary = $"Outbox Ludo: {ok} enviados, {failed} fallidos de {due.Count}.";
        log.LogInformation("[LudoDrainer] {Summary}", summary);
        return failed == 0
            ? JobRunResult.Success(due.Count, summary)
            : JobRunResult.Partial(due.Count, ok, failed, summary);
    }
}
