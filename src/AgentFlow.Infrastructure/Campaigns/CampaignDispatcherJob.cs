using Hangfire;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.Campaigns;

/// <summary>
/// Job de Hangfire que orquesta el envío de campañas.
///
/// Funciona como un "loop controlado":
/// 1. Ejecuta un lote de envíos (max 200 mensajes)
/// 2. Cuando termina el lote, evalúa la razón de parada
/// 3. Se reprograma a sí mismo según la razón:
///    - Lote completado → reprogramar en 5 min (dar descanso al número)
///    - Fuera de horario → reprogramar para mañana a las 8am
///    - Límite diario → reprogramar para mañana a las 8am
///    - Muchos errores → reprogramar en 30 min (esperar que pase el ban)
///    - Campaña completada → no se reprograma (terminó)
///
/// Hangfire garantiza que si el servidor se cae, el job se retoma al reiniciar.
/// </summary>
public class CampaignDispatcherJob(
    CampaignDispatcherService dispatcher,
    IBackgroundJobClient jobClient,
    ILogger<CampaignDispatcherJob> logger)
{
    /// <summary>
    /// Método que Hangfire ejecuta. Recibe el ID de la campaña.
    /// </summary>
    [AutomaticRetry(Attempts = 1)]  // si falla, reintenta 1 vez
    public async Task ExecuteAsync(Guid campaignId, CancellationToken ct)
    {
        logger.LogInformation("CampaignJob: iniciando lote para campaña {CampaignId}", campaignId);

        var result = await dispatcher.DispatchBatchAsync(campaignId, ct);

        logger.LogInformation(
            "CampaignJob: campaña {CampaignId} - Enviados={Sent}, Fallidos={Failed}, Razón={Reason}",
            campaignId, result.Sent, result.Failed, result.StopReason);

        // ── Decidir si reprogramar ───────────────────────
        switch (result.StopReason)
        {
            case DispatchStopReason.BatchCompleted:
                // Terminó el lote pero quedan contactos → reprogramar en 5 min
                ScheduleNext(campaignId, TimeSpan.FromMinutes(5), "lote completado, quedan contactos");
                break;

            case DispatchStopReason.OutsideBusinessHours:
            case DispatchStopReason.DailyLimitReached:
                // Fuera de horario o límite diario → mañana a las 8am
                ScheduleNextBusinessDay(campaignId);
                break;

            case DispatchStopReason.TooManyErrors:
                // Posible ban → esperar 30 minutos
                ScheduleNext(campaignId, TimeSpan.FromMinutes(30), "errores consecutivos, pausando");
                break;

            case DispatchStopReason.AllContactsProcessed:
                logger.LogInformation("CampaignJob: campaña {CampaignId} completada. No se reprograma.", campaignId);
                break;

            case DispatchStopReason.CampaignInactive:
            case DispatchStopReason.NoProvider:
            case DispatchStopReason.Error:
                logger.LogWarning("CampaignJob: campaña {CampaignId} detenida por {Reason}. No se reprograma.",
                    campaignId, result.StopReason);
                break;
        }
    }

    /// <summary>
    /// Programa el siguiente lote después de un delay.
    /// </summary>
    private void ScheduleNext(Guid campaignId, TimeSpan delay, string reason)
    {
        logger.LogInformation("CampaignJob: reprogramando campaña {CampaignId} en {Delay} ({Reason})",
            campaignId, delay, reason);

        jobClient.Schedule<CampaignDispatcherJob>(
            job => job.ExecuteAsync(campaignId, CancellationToken.None),
            delay);
    }

    /// <summary>
    /// Programa el siguiente lote para mañana a las 8am (hora del tenant).
    /// Simplificación: usa 8:00 AM UTC-5 (Panamá).
    /// </summary>
    private void ScheduleNextBusinessDay(Guid campaignId)
    {
        // Panamá = UTC-5
        var panamaOffset = TimeSpan.FromHours(-5);
        var nowPanama = DateTimeOffset.UtcNow.ToOffset(panamaOffset);
        var tomorrow8am = nowPanama.Date.AddDays(1).Add(new TimeSpan(8, 0, 0));
        var tomorrowUtc = new DateTimeOffset(tomorrow8am, panamaOffset).UtcDateTime;
        var delay = tomorrowUtc - DateTime.UtcNow;

        if (delay < TimeSpan.Zero)
            delay = TimeSpan.FromMinutes(5); // fallback

        logger.LogInformation(
            "CampaignJob: reprogramando campaña {CampaignId} para mañana 8am Panamá (en {Delay})",
            campaignId, delay);

        jobClient.Schedule<CampaignDispatcherJob>(
            job => job.ExecuteAsync(campaignId, CancellationToken.None),
            delay);
    }
}
