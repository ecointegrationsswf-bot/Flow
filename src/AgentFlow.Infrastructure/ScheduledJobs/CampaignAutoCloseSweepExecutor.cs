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

        // Cargamos campañas Running con su template (necesitamos AutoCloseHours).
        var runningCampaigns = await db.Campaigns
            .Where(c => c.IsActive
                     && c.Status == CampaignStatus.Running
                     && c.CampaignTemplateId != null)
            .OrderBy(c => c.LaunchedAt ?? c.CreatedAt)
            .Take(MaxCampaignsPerTick)
            .ToListAsync(ct);

        if (runningCampaigns.Count == 0)
            return JobRunResult.Skipped("Sin campañas Running.");

        var templateIds = runningCampaigns.Select(c => c.CampaignTemplateId!.Value).Distinct().ToList();
        var templates = await db.CampaignTemplates
            .Where(t => templateIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, ct);

        var totalClosed = 0;
        var campaignsTouched = 0;
        var failures = 0;

        foreach (var campaign in runningCampaigns)
        {
            if (ct.IsCancellationRequested) break;
            if (!templates.TryGetValue(campaign.CampaignTemplateId!.Value, out var template)) continue;
            if (template.AutoCloseHours <= 0) continue;

            // El "reloj" de auto-cierre arranca con la campaña; usamos LaunchedAt
            // o, en su defecto, CreatedAt. Cuando una conversación tiene actividad
            // posterior, no se cierra hasta que esa conversación supere su propio
            // umbral por inactividad — eso lo gestiona el campo Conversation.LastActivityAt.
            var campaignDueAt = (campaign.LaunchedAt ?? campaign.CreatedAt)
                                    .AddHours(template.AutoCloseHours);
            if (nowUtc < campaignDueAt) continue; // todavía dentro de la ventana

            try
            {
                var (closed, failed) = await CloseCampaignAsync(campaign, template, ctx, ct);
                totalClosed += closed;
                failures += failed;
                if (closed > 0) campaignsTouched++;
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

        var summary = $"Closed={totalClosed} convs en {campaignsTouched} campañas · failures={failures} · scanned={runningCampaigns.Count}";
        log.LogInformation("[AutoCloseSweep] {Summary}", summary);

        if (totalClosed == 0 && failures == 0)
            return JobRunResult.Skipped(summary);
        if (failures > 0 && totalClosed == 0)
            return JobRunResult.Failed(summary);
        return JobRunResult.Success(totalClosed, summary);
    }

    private async Task<(int closed, int failed)> CloseCampaignAsync(
        Campaign campaign, CampaignTemplate template, ScheduledJobContext ctx, CancellationToken ct)
    {
        var activeConvs = await db.Conversations
            .Where(c => c.CampaignId == campaign.Id
                     && c.Status == ConversationStatus.Active)
            .ToListAsync(ct);
        if (activeConvs.Count == 0) return (0, 0);

        var autoCloseMessage = template.AutoCloseMessage;
        var provider = string.IsNullOrEmpty(autoCloseMessage)
            ? null
            : await providerFactory.GetProviderAsync(campaign.TenantId, ct);

        var success = 0;
        var failure = 0;

        foreach (var conv in activeConvs)
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
        await db.SaveChangesAsync(ct);

        foreach (var conv in activeConvs.Where(c => c.Status == ConversationStatus.Closed))
        {
            try { await eventDispatcher.DispatchAsync("ConversationClosed", conv.Id.ToString(), campaign.TenantId, ct); }
            catch (Exception ex) { log.LogWarning(ex, "[AutoCloseSweep] no pude disparar ConversationClosed."); }
        }

        var stillActive = await db.Conversations
            .CountAsync(c => c.CampaignId == campaign.Id && c.Status == ConversationStatus.Active, ct);
        if (stillActive == 0)
        {
            try { await eventDispatcher.DispatchAsync("CampaignFinished", campaign.Id.ToString(), campaign.TenantId, ct); }
            catch (Exception ex) { log.LogWarning(ex, "[AutoCloseSweep] no pude disparar CampaignFinished."); }
        }

        return (success, failure);
    }
}
