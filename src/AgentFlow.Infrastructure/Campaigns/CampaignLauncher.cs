using System.Text.Json;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.Campaigns;

/// <summary>
/// Implementación de ICampaignLauncher. Encapsula la lógica que originalmente vivía
/// en CampaignsController.Launch para que pueda invocarse también desde procesos
/// automáticos (ej: DelinquencyProcessor cuando AutoCrearCampanas=true).
/// </summary>
public class CampaignLauncher(
    AgentFlowDbContext db,
    IConfiguration cfg,
    IHttpClientFactory httpClientFactory,
    ILogger<CampaignLauncher> logger) : ICampaignLauncher
{
    public async Task<CampaignLaunchResult> LaunchAsync(
        Guid campaignId,
        string launchedByUserId,
        string? launchedByUserPhone,
        CancellationToken ct = default)
    {
        var campaign = await db.Campaigns
            .Include(c => c.CampaignTemplate)
            .Include(c => c.Tenant)
            .FirstOrDefaultAsync(c => c.Id == campaignId, ct);

        if (campaign is null)
            return new(false, campaignId, "NotFound", 0, null, "Campaña no encontrada.");
        if (campaign.Status == CampaignStatus.Running)
            return new(false, campaignId, campaign.Status.ToString(), 0, null, "La campaña ya está en ejecución.");
        if (campaign.Status == CampaignStatus.Completed)
            return new(false, campaignId, campaign.Status.ToString(), 0, null, "La campaña ya fue completada.");

        var pendingCount = await db.CampaignContacts
            .CountAsync(c => c.CampaignId == campaignId
                          && c.IsPhoneValid
                          && c.DispatchStatus == DispatchStatus.Pending, ct);
        if (pendingCount == 0)
            return new(false, campaignId, campaign.Status.ToString(), 0, null, "La campaña no tiene contactos válidos pendientes.");

        if (campaign.CampaignTemplate is null)
            return new(false, campaignId, campaign.Status.ToString(), pendingCount, null, "La campaña no tiene un maestro asociado.");
        if (campaign.CampaignTemplate.PromptTemplateIds is null ||
            campaign.CampaignTemplate.PromptTemplateIds.Count == 0)
            return new(false, campaignId, campaign.Status.ToString(), pendingCount, null, "El maestro no tiene un prompt vinculado.");

        // Resolver credenciales WhatsApp
        var instanceId = campaign.Tenant.WhatsAppInstanceId;
        var apiToken   = campaign.Tenant.WhatsAppApiToken;

        if (string.IsNullOrEmpty(instanceId) || string.IsNullOrEmpty(apiToken))
        {
            var activeLine = await db.WhatsAppLines
                .Where(l => l.TenantId == campaign.TenantId && l.IsActive)
                .OrderByDescending(l => l.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (activeLine is null)
                return new(false, campaignId, campaign.Status.ToString(), pendingCount, null,
                    "El tenant no tiene WhatsApp configurado.");

            instanceId = activeLine.InstanceId;
            apiToken   = activeLine.ApiToken;
        }

        campaign.Status              = CampaignStatus.Launching;
        campaign.LaunchedAt          = DateTime.UtcNow;
        campaign.LaunchedByUserId    = launchedByUserId;
        campaign.LaunchedByUserPhone = launchedByUserPhone;
        await db.SaveChangesAsync(ct);

        var webhookUrl = cfg["N8n:CampaignWebhookUrl"];
        if (!string.IsNullOrEmpty(webhookUrl))
        {
            try
            {
                var contacts = await db.CampaignContacts
                    .Where(c => c.CampaignId == campaignId
                             && c.IsPhoneValid
                             && c.DispatchStatus == DispatchStatus.Pending)
                    .Select(c => new
                    {
                        phone           = c.PhoneNumber,
                        clientName      = c.ClientName,
                        policyNumber    = c.PolicyNumber,
                        pendingAmount   = c.PendingAmount,
                        insurance       = c.InsuranceCompany,
                        contactDataJson = c.ContactDataJson,
                    })
                    .ToListAsync(ct);

                var httpClient = httpClientFactory.CreateClient();
                var payload = JsonSerializer.Serialize(new
                {
                    campaignId          = campaign.Id,
                    agentId             = campaign.AgentDefinitionId,
                    warmupDay           = 0,
                    messageDelaySeconds = campaign.Tenant.CampaignMessageDelaySeconds,
                    tenantConfig        = new
                    {
                        tenantId           = campaign.TenantId,
                        ultraMsgInstanceId = instanceId,
                        ultraMsgToken      = apiToken,
                    },
                    contacts,
                });
                var resp = await httpClient.PostAsync(webhookUrl,
                    new StringContent(payload, System.Text.Encoding.UTF8, "application/json"), ct);

                if (!resp.IsSuccessStatusCode)
                {
                    var body = await resp.Content.ReadAsStringAsync(ct);
                    campaign.Status = CampaignStatus.Failed;
                    await db.SaveChangesAsync(ct);
                    return new(false, campaignId, campaign.Status.ToString(), pendingCount, campaign.LaunchedAt,
                        $"n8n rechazó la solicitud ({(int)resp.StatusCode}). {body[..Math.Min(200, body.Length)]}");
                }

                await db.Entry(campaign).ReloadAsync(ct);
                if (campaign.Status == CampaignStatus.Launching)
                    campaign.Status = CampaignStatus.Running;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[CampaignLauncher] Error disparando n8n para campaign {CampaignId}", campaignId);
                campaign.Status = CampaignStatus.Failed;
                await db.SaveChangesAsync(ct);
                return new(false, campaignId, campaign.Status.ToString(), pendingCount, campaign.LaunchedAt,
                    $"No se pudo disparar n8n: {ex.Message}");
            }
        }
        else
        {
            campaign.Status = CampaignStatus.Running; // sin n8n configurado (dev)
        }

        await db.SaveChangesAsync(ct);
        return new(true, campaignId, campaign.Status.ToString(), pendingCount, campaign.LaunchedAt, null);
    }
}
