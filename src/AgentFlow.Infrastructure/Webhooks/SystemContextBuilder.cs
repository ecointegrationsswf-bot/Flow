using AgentFlow.Domain.Webhooks;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.Infrastructure.Webhooks;

/// <summary>
/// Construye el SystemContext leyendo de BD las entidades relacionadas con la conversación actual.
///
/// Mapping de sourceKeys (documento sección 3.2) al modelo real de AgentFlow:
/// - contact.* → viene de CampaignContact (si hay CampaignId) o de Conversation.ClientName/Phone
/// - session.* → viene de BrainSession Redis + conversación actual
/// - campaign.* → Campaign entity
/// - tenant.* → Tenant entity
/// - conversation.* → Conversation entity
/// </summary>
public class SystemContextBuilder(AgentFlowDbContext db) : ISystemContextBuilder
{
    public async Task<SystemContext> BuildAsync(
        Guid tenantId,
        Guid? campaignId,
        string contactPhone,
        Guid? conversationId,
        string? agentSlug = null,
        CancellationToken ct = default)
    {
        var ctx = new SystemContext();

        // ── Tenant ──
        var tenant = await db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.Id, t.Name, t.Slug })
            .FirstOrDefaultAsync(ct);

        if (tenant is not null)
        {
            ctx.Set("tenant.id", tenant.Id.ToString());
            ctx.Set("tenant.name", tenant.Name);
            ctx.Set("tenant.slug", tenant.Slug);
        }

        // ── Conversation (si hay ID) ──
        if (conversationId.HasValue)
        {
            var conv = await db.Conversations
                .Where(c => c.Id == conversationId.Value)
                .Select(c => new { c.Id, c.StartedAt, c.ClientName, c.ClientPhone, c.PolicyNumber })
                .FirstOrDefaultAsync(ct);

            if (conv is not null)
            {
                ctx.Set("conversation.id", conv.Id.ToString());
                ctx.Set("conversation.createdAt", conv.StartedAt.ToString("O"));

                // Datos del contacto via conversation
                if (!string.IsNullOrEmpty(conv.ClientName))
                    ctx.Set("contact.name", conv.ClientName);
                if (!string.IsNullOrEmpty(conv.PolicyNumber))
                    ctx.Set("contact.policyNumber", conv.PolicyNumber);
            }
        }

        // ── Contact (teléfono siempre disponible) ──
        ctx.Set("contact.phone", contactPhone);

        // ── Campaign + CampaignContact (si aplica) ──
        if (campaignId.HasValue)
        {
            var campaign = await db.Campaigns
                .Where(c => c.Id == campaignId.Value)
                .Select(c => new { c.Id, c.Name })
                .FirstOrDefaultAsync(ct);

            if (campaign is not null)
            {
                ctx.Set("campaign.id", campaign.Id.ToString());
                ctx.Set("campaign.name", campaign.Name);
                ctx.Set("campaign.slug", campaign.Name); // AgentFlow no tiene slug en Campaign
            }

            // Buscar el contacto de campaña para obtener datos del Excel
            var campaignContact = await db.CampaignContacts
                .AsNoTracking()
                .Where(cc => cc.CampaignId == campaignId.Value && cc.PhoneNumber == contactPhone)
                .FirstOrDefaultAsync(ct);

            if (campaignContact is not null)
            {
                // Campos fuertemente tipados
                if (!string.IsNullOrEmpty(campaignContact.ClientName))
                    ctx.Set("contact.name", campaignContact.ClientName);
                if (!string.IsNullOrEmpty(campaignContact.Email))
                    ctx.Set("contact.email", campaignContact.Email);
                if (!string.IsNullOrEmpty(campaignContact.PolicyNumber))
                    ctx.Set("contact.policyNumber", campaignContact.PolicyNumber);
                if (!string.IsNullOrEmpty(campaignContact.InsuranceCompany))
                    ctx.Set("contact.insuranceCompany", campaignContact.InsuranceCompany);
                if (campaignContact.PendingAmount.HasValue)
                    ctx.Set("contact.pendingAmount", campaignContact.PendingAmount.Value.ToString("F2"));

                // ContactDataJson: aplanar todas las columnas del Excel como contact.{fieldName}
                if (!string.IsNullOrEmpty(campaignContact.ContactDataJson))
                {
                    try
                    {
                        var records = System.Text.Json.JsonSerializer
                            .Deserialize<List<Dictionary<string, System.Text.Json.JsonElement>>>(
                                campaignContact.ContactDataJson);

                        if (records is { Count: > 0 })
                        {
                            // Si hay 1 registro, aplanar cada campo al contexto
                            if (records.Count == 1)
                            {
                                foreach (var (key, val) in records[0])
                                {
                                    var strVal = val.ValueKind == System.Text.Json.JsonValueKind.String
                                        ? val.GetString() ?? ""
                                        : val.ToString();
                                    if (!string.IsNullOrEmpty(strVal))
                                        ctx.Set($"contact.{key}", strVal);
                                }
                            }
                            // Múltiples registros: guardar el array completo
                            else
                            {
                                ctx.Set("contact.records", campaignContact.ContactDataJson);
                                ctx.Set("contact.totalRecords", records.Count.ToString());
                            }
                        }
                    }
                    catch { /* JSON inválido — ignorar */ }
                }
            }
        }

        // ── Session (datos sintéticos del estado actual) ──
        if (!string.IsNullOrEmpty(agentSlug))
            ctx.Set("session.agentSlug", agentSlug);

        ctx.Set("session.origin", campaignId.HasValue ? "Campaign" : "Inbound");
        if (conversationId.HasValue)
            ctx.Set("session.id", conversationId.Value.ToString());

        return ctx;
    }
}
