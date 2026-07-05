using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.ScheduledJobs;

/// <summary>
/// Escalamiento robusto — Fase E (watchdog de conversaciones escaladas sin atender).
///
/// Barre las conversaciones que quedaron <c>EscalatedToHuman</c> pero que ningún humano tomó
/// (<c>IsHumanHandled=false</c>, <c>HandledByUserId</c> nulo) y que llevan un rato sin actividad,
/// y RE-NOTIFICA al ejecutivo (recordatorio) saltando el cooldown vía
/// <see cref="ITransferChatService.ExecuteIfApplicableAsync"/> con <c>forceRenotify=true</c>.
///
/// Genérico y acotado: solo aplica a tenants con <c>Tenant.KeepAiActiveUntilTakeover=true</c>
/// (los que adoptaron el escalamiento sin-silencio; en los demás la auto-escalada ya pausa al
/// agente y el flujo es el histórico). Reusa toda la lógica de notificación de TransferChatService.
///
/// Slug: <c>ESCALATED_WATCHDOG</c>. Se dispara con un ScheduledWebhookJob cron (ej: cada 15 min).
/// </summary>
public class EscalatedConversationWatchdogExecutor(
    AgentFlowDbContext db,
    ITransferChatService transferChat,
    ILogger<EscalatedConversationWatchdogExecutor> log) : IScheduledJobExecutor
{
    public string Slug => "ESCALATED_WATCHDOG";

    private const int MaxPerTick = 50;
    /// <summary>Minutos sin actividad para considerar una escalada "olvidada".</summary>
    private const int StaleThresholdMinutes = 15;
    /// <summary>Ventana mínima entre recordatorios al ejecutivo (12h ⇒ a lo sumo ~1 recordatorio por conversación).</summary>
    private const int ReminderWindowMinutes = 720;
    /// <summary>
    /// COTA SUPERIOR de antigüedad: solo se recuerdan escaladas RECIENTES (≤24h). Sin esto, el
    /// watchdog "resucitaba" conversaciones escaladas de hace semanas (abandonadas) y spameaba al
    /// ejecutivo — bug detectado el 2026-06-16 (SOMOS recibió correos de conversaciones de mayo).
    /// Una escalada sin atender de >24h se considera abandonada: NO se re-notifica.
    /// </summary>
    private const int MaxAgeHours = 24;

    public async Task<JobRunResult> ExecuteAsync(ScheduledWebhookJob job, ScheduledJobContext ctx, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var staleCutoff = now.AddMinutes(-StaleThresholdMinutes);
        var reminderCutoff = now.AddMinutes(-ReminderWindowMinutes);
        var maxAgeCutoff = now.AddHours(-MaxAgeHours);

        // Tenants que adoptaron el escalamiento sin-silencio.
        var enabledTenantIds = await db.Tenants
            .Where(t => t.KeepAiActiveUntilTakeover)
            .Select(t => t.Id)
            .ToListAsync(ct);
        if (enabledTenantIds.Count == 0)
            return JobRunResult.Skipped("Ningún tenant con KeepAiActiveUntilTakeover.");

        // Escaladas sin atender, RECIENTES (entre 15 min y 24h), no recordadas dentro de la ventana.
        // La cota superior (maxAgeCutoff) es clave: NO resucita conversaciones viejas/abandonadas.
        var candidates = await db.Conversations
            .Where(c => enabledTenantIds.Contains(c.TenantId)
                     && c.Status == ConversationStatus.EscalatedToHuman
                     && !c.IsHumanHandled
                     && c.LastActivityAt < staleCutoff
                     && c.LastActivityAt > maxAgeCutoff
                     && (c.LastTransferChatSentAt == null || c.LastTransferChatSentAt < reminderCutoff))
            .OrderBy(c => c.LastActivityAt)
            .Take(MaxPerTick)
            .ToListAsync(ct);

        if (candidates.Count == 0)
            return JobRunResult.Skipped("Sin escaladas pendientes de recordatorio.");

        int renotified = 0, skipped = 0, failures = 0;
        foreach (var conv in candidates)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                // forceRenotify=true: salta el cooldown y re-notifica + actualiza LastTransferChatSentAt.
                var notified = await transferChat.ExecuteIfApplicableAsync(conv, ct, forceRenotify: true);
                if (notified) renotified++;
                else skipped++;   // p.ej. el maestro no tiene TRANSFER_CHAT vinculada
            }
            catch (Exception ex)
            {
                failures++;
                log.LogError(ex, "[EscalatedWatchdog] Error renotificando conversación {ConvId}", conv.Id);
            }
        }

        var summary = $"Recordados={renotified} | Saltados={skipped} | Fallos={failures} | Escaneados={candidates.Count}";
        log.LogInformation("[EscalatedWatchdog] {Summary}", summary);

        if (failures > 0 && renotified == 0)
            return JobRunResult.Failed(summary);
        if (failures > 0)
            return JobRunResult.Partial(candidates.Count, renotified, failures, summary);
        return JobRunResult.Success(renotified, summary);
    }
}
