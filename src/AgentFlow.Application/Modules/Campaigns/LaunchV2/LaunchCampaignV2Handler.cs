using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Helpers;
using AgentFlow.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Application.Modules.Campaigns.LaunchV2;

/// <summary>
/// Handler del lanzamiento v2. Reemplaza la orquestación que hoy ocurre en n8n
/// (workflow_campana_v3) por lógica en proceso. El envío real lo hace el
/// CampaignWorker en el Worker Service consumiendo los contactos en estado Queued.
/// </summary>
public class LaunchCampaignV2Handler(
    ICampaignRepository campaigns,
    IDuplicateChecker duplicateChecker,
    ILogger<LaunchCampaignV2Handler> logger
) : IRequestHandler<LaunchCampaignV2Command, LaunchCampaignV2Result>
{
    public async Task<LaunchCampaignV2Result> Handle(LaunchCampaignV2Command cmd, CancellationToken ct)
    {
        var campaign = await campaigns.GetByIdWithTenantAsync(cmd.CampaignId, cmd.TenantId, ct);
        if (campaign is null)
            return Fail(cmd, "NotFound", "Campaña no encontrada.");

        if (campaign.Tenant is null)
            return Fail(cmd, campaign.Status.ToString(),
                "El tenant asociado a la campaña no existe.");

        // Una campaña en Running/Completed/Failed/Paused no se relanza desde acá.
        if (campaign.Status is CampaignStatus.Running or CampaignStatus.Launching)
            return Fail(cmd, campaign.Status.ToString(),
                "La campaña ya está en ejecución.");
        if (campaign.Status == CampaignStatus.Completed)
            return Fail(cmd, campaign.Status.ToString(),
                "La campaña ya fue completada.");
        if (campaign.Status == CampaignStatus.Paused)
            return Fail(cmd, campaign.Status.ToString(),
                "La campaña está pausada — usar /resume.");

        // ── Fase A: contactos ya validados por StartCampaignCommand ──
        // Marcamos como Skipped los inválidos / duplicados-en-archivo (IsPhoneValid=false)
        // para que el Worker no los recoja. Son terminales.
        int skippedCount = 0;
        foreach (var c in campaign.Contacts.Where(c => !c.IsPhoneValid))
        {
            if (c.DispatchStatus == DispatchStatus.Pending)
            {
                c.DispatchStatus = DispatchStatus.Skipped;
                skippedCount++;
            }
        }

        // Candidatos a despacho: válidos y aún sin procesar
        var candidates = campaign.Contacts
            .Where(c => c.IsPhoneValid && c.DispatchStatus == DispatchStatus.Pending)
            .OrderBy(c => c.CreatedAt)
            .ToList();

        if (candidates.Count == 0)
            return Fail(cmd, campaign.Status.ToString(),
                "La campaña no tiene contactos válidos pendientes.");

        // ── Fase B: detectar duplicados activos contra otras campañas del tenant ──
        var activePhones = await duplicateChecker.GetActivePhonesAsync(
            cmd.TenantId, cmd.CampaignId,
            candidates.Select(c => c.PhoneNumber),
            ct);

        int duplicateCount = 0;
        var nonDuplicates = new List<Domain.Entities.CampaignContact>(candidates.Count);
        foreach (var c in candidates)
        {
            if (activePhones.Contains(c.PhoneNumber))
            {
                c.DispatchStatus = DispatchStatus.Duplicate;
                duplicateCount++;
            }
            else
            {
                nonDuplicates.Add(c);
            }
        }

        // ── Fase C: aplicar warm-up ──
        var dailyLimit = WarmupLimiter.DailyLimitFor(cmd.WarmupDay);
        var scheduledForUtc = DeferredScheduler.ComputeNextRunUtc(
            campaign.Tenant.TimeZone,
            campaign.Tenant.BusinessHoursStart,
            DateTime.UtcNow);

        int queuedCount = 0;
        int deferredCount = 0;
        for (int i = 0; i < nonDuplicates.Count; i++)
        {
            var contact = nonDuplicates[i];
            if (i < dailyLimit)
            {
                contact.DispatchStatus = DispatchStatus.Queued;
                contact.ScheduledFor = null;     // inmediato
                queuedCount++;
            }
            else
            {
                contact.DispatchStatus = DispatchStatus.Deferred;
                contact.ScheduledFor = scheduledForUtc;
                deferredCount++;
            }
        }

        // ── Fase D: actualizar la campaña ──
        var launchedAt = DateTime.UtcNow;
        campaign.Status = CampaignStatus.Running;
        campaign.LaunchedAt = launchedAt;
        campaign.LaunchedByUserId = cmd.LaunchedByUserId;
        campaign.LaunchedByUserPhone = cmd.LaunchedByUserPhone;
        campaign.IsActive = true;

        await campaigns.UpdateAsync(campaign, ct);

        logger.LogInformation(
            "Campaign {CampaignId} (tenant {TenantId}) launched v2 — Queued={Queued} Deferred={Deferred} Duplicate={Duplicate} Skipped={Skipped} (warmupDay={WarmupDay}, dailyLimit={DailyLimit})",
            cmd.CampaignId, cmd.TenantId, queuedCount, deferredCount, duplicateCount, skippedCount,
            cmd.WarmupDay, dailyLimit);

        return new LaunchCampaignV2Result(
            Success: true,
            CampaignId: cmd.CampaignId,
            Status: campaign.Status.ToString(),
            TotalProcessed: queuedCount + deferredCount + duplicateCount + skippedCount,
            QueuedCount: queuedCount,
            DeferredCount: deferredCount,
            DuplicateCount: duplicateCount,
            SkippedCount: skippedCount,
            DailyLimit: dailyLimit,
            WarmupDay: cmd.WarmupDay,
            LaunchedAt: launchedAt,
            Error: null);
    }

    private static LaunchCampaignV2Result Fail(LaunchCampaignV2Command cmd, string status, string error) =>
        new(false, cmd.CampaignId, status, 0, 0, 0, 0, 0, 0, cmd.WarmupDay, null, error);
}
