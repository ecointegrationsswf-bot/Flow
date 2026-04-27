using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.ScheduledJobs;

/// <summary>
/// Executor del job AUTO_CLOSE_CAMPAIGN. Cierra todas las conversaciones activas
/// de una campaña en lote al cumplirse CampaignTemplate.AutoCloseHours desde el
/// inicio de la campaña.
///
/// El ContextId es el CampaignId. Se invoca una sola vez por campaña gracias al
/// scope PerCampaign del ScheduledWebhookJob.
///
/// Comportamiento:
/// 1. Carga conversaciones Active de la campaña.
/// 2. Para cada una: envía AutoCloseMessage (si está configurado) y marca Closed.
/// 3. Dispara ConversationClosed por cada conversación cerrada (Fase 3 etiquetará).
/// 4. Si todas se cerraron y la campaña ya no tiene activas, dispara CampaignFinished.
///
/// Idempotente: si la campaña ya no tiene conversaciones activas, devuelve Skipped.
/// </summary>
public class CampaignAutoCloseExecutor(
    AgentFlowDbContext db,
    IChannelProviderFactory providerFactory,
    IWebhookEventDispatcher eventDispatcher,
    ILogger<CampaignAutoCloseExecutor> log) : IScheduledJobExecutor
{
    public string Slug => "AUTO_CLOSE_CAMPAIGN";

    public async Task<JobRunResult> ExecuteAsync(
        ScheduledWebhookJob job, ScheduledJobContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ctx.ContextId) || !Guid.TryParse(ctx.ContextId, out var campaignId))
            return JobRunResult.Skipped("ContextId requerido (CampaignId).");

        var campaign = await db.Campaigns
            .FirstOrDefaultAsync(c => c.Id == campaignId, ct);
        if (campaign is null)
            return JobRunResult.Skipped($"Campaña {campaignId} no existe.");

        if (!campaign.IsActive)
            return JobRunResult.Skipped("Campaña ya cancelada.");

        var template = await db.CampaignTemplates
            .FirstOrDefaultAsync(t => t.Id == campaign.CampaignTemplateId, ct);
        var autoCloseMessage = template?.AutoCloseMessage;

        var activeConvs = await db.Conversations
            .Where(c => c.CampaignId == campaignId
                        && c.Status == ConversationStatus.Active)
            .ToListAsync(ct);

        if (activeConvs.Count == 0)
            return JobRunResult.Skipped("Sin conversaciones activas para cerrar.");

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
                // Enviar mensaje de cierre si está configurado y hay provider.
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
                    else
                    {
                        log.LogWarning("AutoClose: envío de mensaje falló para conv {Conv}: {Err}",
                            conv.Id, send.Error);
                    }
                }

                conv.Status = ConversationStatus.Closed;
                conv.ClosedAt = DateTime.UtcNow;
                conv.LastActivityAt = DateTime.UtcNow;

                success++;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "AutoClose: error cerrando conversación {Conv}", conv.Id);
                failure++;
            }
        }

        await db.SaveChangesAsync(ct);

        // Disparar ConversationClosed por cada conversación cerrada (Fase 3 etiquetará).
        foreach (var conv in activeConvs.Where(c => c.Status == ConversationStatus.Closed))
        {
            try { await eventDispatcher.DispatchAsync("ConversationClosed", conv.Id.ToString(), campaign.TenantId, ct); }
            catch (Exception ex) { log.LogWarning(ex, "AutoClose: no pude disparar ConversationClosed para {Conv}.", conv.Id); }
        }

        // Si la campaña quedó sin conversaciones activas, también disparamos CampaignFinished.
        var stillActive = await db.Conversations
            .CountAsync(c => c.CampaignId == campaignId && c.Status == ConversationStatus.Active, ct);
        if (stillActive == 0)
        {
            try { await eventDispatcher.DispatchAsync("CampaignFinished", campaignId.ToString(), campaign.TenantId, ct); }
            catch (Exception ex) { log.LogWarning(ex, "AutoClose: no pude disparar CampaignFinished."); }
        }

        var summary = $"AutoClose: {success}/{activeConvs.Count} OK · {failure} fallos.";
        log.LogInformation("Campaña {Cmp} {Summary}", campaignId, summary);

        if (failure == 0)
            return JobRunResult.Success(activeConvs.Count, summary);
        if (success > 0)
            return JobRunResult.Partial(activeConvs.Count, success, failure, summary);
        return JobRunResult.Failed("AutoClose falló para todas las conversaciones.", summary);
    }
}
