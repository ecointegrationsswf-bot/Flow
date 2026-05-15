using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.ScheduledJobs;

/// <summary>
/// Sweeper global de auto-cierre. UN solo job cron en la tabla
/// (Cron */30 * * * *, Scope=AllTenants) que escanea TODAS las campañas
/// Running y cierra las que ya excedieron CampaignTemplate.AutoCloseHours
/// desde su última actividad.
///
/// Reemplaza el modelo anterior "1 job DelayFromEvent por campaña", que llenaba
/// la tabla ScheduledWebhookJobs con un job por cada campaña creada.
/// </summary>
public class CampaignAutoCloseSweepExecutor(
    AgentFlowDbContext db,
    IChannelProviderFactory providerFactory,
    IWebhookEventDispatcher eventDispatcher,
    JobExecutionAuditor auditor,
    ILogger<CampaignAutoCloseSweepExecutor> log) : IScheduledJobExecutor
{
    public string Slug => "AUTO_CLOSE_CAMPAIGN_SWEEP";

    private const int MaxCampaignsPerTick = 50;

    public async Task<JobRunResult> ExecuteAsync(
        ScheduledWebhookJob job, ScheduledJobContext ctx, CancellationToken ct)
    {
        var nowUtc = DateTime.UtcNow;

        // Cargamos campañas vivas (Running o Completed) con su template — necesitamos
        // AutoCloseHours. Incluir Completed es CRÍTICO: el dispatcher marca
        // Status=Completed apenas termina de mandar los mensajes iniciales, pero
        // la campaña sigue viva (IsActive=true) durante la ventana de follow-ups y
        // hasta que se cumplan AutoCloseHours desde CompletedAt. Si solo
        // filtráramos por Running, las campañas Completed quedarían colgadas para
        // siempre y nunca pasarían a Closed → el "Cierre automático" del maestro
        // no se reflejaría en la vista de Campañas.
        var liveCampaigns = await db.Campaigns
            .Where(c => c.IsActive
                     && (c.Status == CampaignStatus.Running
                         || c.Status == CampaignStatus.Completed)
                     && c.CampaignTemplateId != null)
            .OrderBy(c => c.CompletedAt ?? c.LaunchedAt ?? c.CreatedAt)
            .Take(MaxCampaignsPerTick)
            .ToListAsync(ct);

        if (liveCampaigns.Count == 0)
            return JobRunResult.Skipped("Sin campañas vivas (Running/Completed) para evaluar.");

        var templateIds = liveCampaigns.Select(c => c.CampaignTemplateId!.Value).Distinct().ToList();
        var templates = await db.CampaignTemplates
            .Where(t => templateIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, ct);

        var totalClosed = 0;
        var campaignsTouched = 0;
        var failures = 0;

        foreach (var campaign in liveCampaigns)
        {
            if (ct.IsCancellationRequested) break;
            if (!templates.TryGetValue(campaign.CampaignTemplateId!.Value, out var template)) continue;
            if (template.AutoCloseHours <= 0) continue;

            // El reloj de auto-cierre arranca DESPUÉS de que terminó el dispatch
            // inicial (CompletedAt). Esto matchea el label del UI: "Horas después de
            // enviada la campaña para cerrar automáticamente". Si la campaña todavía
            // está en Running (CompletedAt == null), caemos a LaunchedAt como
            // fallback para no esperar indefinidamente; y a CreatedAt para casos
            // legacy donde LaunchedAt nunca se setteó.
            var anchorUtc = campaign.CompletedAt
                              ?? campaign.LaunchedAt
                              ?? campaign.CreatedAt;
            var campaignDueAt = anchorUtc.AddHours(template.AutoCloseHours);
            if (nowUtc < campaignDueAt) continue; // todavía dentro de la ventana

            try
            {
                var (closed, failed) = await CloseCampaignAsync(campaign, template, ctx, ct);
                totalClosed += closed;
                failures += failed;
                // Contamos la campaña como "tocada" si la cerramos efectivamente
                // (Status=Closed), independiente de si hubo o no conversaciones
                // que cerrar. Antes solo contaba si había convs activas — fallaba
                // el caso típico donde todos los contactos están en WaitingClient.
                campaignsTouched++;
            }
            catch (Exception ex)
            {
                failures++;
                log.LogError(ex, "[AutoCloseSweep] Error cerrando campaña {Id}", campaign.Id);
                auditor.RecordFailure(
                    ctx.ExecutionId, campaign.TenantId,
                    JobExecutionAuditor.ContextTypes.Campaign,
                    campaign.Id.ToString(), campaign.Name, ex.Message);
            }
        }

        await auditor.FlushAsync(ct);

        var summary = $"Closed={totalClosed} convs en {campaignsTouched} campañas · failures={failures} · scanned={liveCampaigns.Count}";
        log.LogInformation("[AutoCloseSweep] {Summary}", summary);

        // Si cerramos al menos una campaña, es Success aunque no hubiera convs que cerrar.
        if (campaignsTouched == 0 && failures == 0)
            return JobRunResult.Skipped(summary);
        if (failures > 0 && campaignsTouched == 0)
            return JobRunResult.Failed(summary);
        // El conteo principal del Success es de campañas cerradas. La línea de
        // summary ya muestra el detalle de convs cerradas + failures.
        return JobRunResult.Success(campaignsTouched, summary);
    }

    private async Task<(int closed, int failed)> CloseCampaignAsync(
        Campaign campaign, CampaignTemplate template, ScheduledJobContext ctx, CancellationToken ct)
    {
        // Cerramos también conversaciones que sigan esperando al cliente — un cliente
        // que jamás respondió mantiene la conversación en WaitingClient, no en Active.
        // Si solo cerramos Active dejábamos un montón de conversaciones colgando.
        var openConvs = await db.Conversations
            .Where(c => c.CampaignId == campaign.Id
                     && (c.Status == ConversationStatus.Active
                         || c.Status == ConversationStatus.WaitingClient))
            .ToListAsync(ct);

        var autoCloseMessage = template.AutoCloseMessage;
        var provider = string.IsNullOrEmpty(autoCloseMessage) || openConvs.Count == 0
            ? null
            : await providerFactory.GetProviderAsync(campaign.TenantId, ct);

        var success = 0;
        var failure = 0;

        foreach (var conv in openConvs)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                if (provider is not null && !string.IsNullOrEmpty(autoCloseMessage))
                {
                    var send = await provider.SendMessageAsync(
                        new SendMessageRequest(conv.ClientPhone, autoCloseMessage), ct);
                    if (send.Success)
                    {
                        db.Messages.Add(new Message
                        {
                            Id = Guid.NewGuid(),
                            ConversationId = conv.Id,
                            Direction = MessageDirection.Outbound,
                            Status = MessageStatus.Sent,
                            Content = autoCloseMessage,
                            ExternalMessageId = send.ExternalMessageId,
                            IsFromAgent = true,
                            AgentName = "AutoClose",
                            SentAt = DateTime.UtcNow
                        });
                    }
                }
                conv.Status = ConversationStatus.Closed;
                conv.ClosedAt = DateTime.UtcNow;
                conv.LastActivityAt = DateTime.UtcNow;
                success++;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "[AutoCloseSweep] Error cerrando conversación {Conv}", conv.Id);
                failure++;
                auditor.RecordFailure(
                    ctx.ExecutionId, campaign.TenantId,
                    JobExecutionAuditor.ContextTypes.Conversation,
                    conv.Id.ToString(),
                    conv.ClientName ?? conv.ClientPhone,
                    ex.Message);
            }
        }

        // ── Cierre formal de la campaña ──────────────────────────────────────
        // Esto es lo que hace que aparezca "Cerrada" en la vista de Campañas.
        // Antes del fix, el sweeper cerraba las conversaciones pero NUNCA tocaba
        // los flags de la campaña, así que la UI mostraba "Completada" para
        // siempre. Ahora flipamos a Closed + IsActive=false.
        // Una vez IsActive=false, el FollowUpSweepExecutor deja de tomar sus
        // contactos (filtro c.Campaign.IsActive) → no más seguimientos.
        // (El timestamp del cierre queda registrado en el audit log del job.)
        campaign.Status   = CampaignStatus.Closed;
        campaign.IsActive = false;

        await db.SaveChangesAsync(ct);

        foreach (var conv in openConvs.Where(c => c.Status == ConversationStatus.Closed))
        {
            try { await eventDispatcher.DispatchAsync("ConversationClosed", conv.Id.ToString(), campaign.TenantId, ct); }
            catch (Exception ex) { log.LogWarning(ex, "[AutoCloseSweep] no pude disparar ConversationClosed."); }
        }

        try { await eventDispatcher.DispatchAsync("CampaignFinished", campaign.Id.ToString(), campaign.TenantId, ct); }
        catch (Exception ex) { log.LogWarning(ex, "[AutoCloseSweep] no pude disparar CampaignFinished."); }

        return (success, failure);
    }
}
