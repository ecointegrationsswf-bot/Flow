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

    public async Task<SystemContext> BuildResultContextAsync(Guid conversationId, CancellationToken ct = default)
    {
        // 1. Cargar conversación + label en una sola query.
        var convInfo = await db.Conversations
            .Where(c => c.Id == conversationId)
            .Select(c => new
            {
                c.Id, c.TenantId, c.CampaignId, c.ClientPhone, c.ClientName,
                c.Status, c.ClosedAt, c.IsHumanHandled,
                c.LabelId, c.LabeledAt,
                LabelName = c.Label != null ? c.Label.Name : null,
                LabelKeywords = c.Label != null ? c.Label.Keywords : null,
                MessageCount = c.Messages.Count,
            })
            .FirstOrDefaultAsync(ct);

        if (convInfo is null)
        {
            // Sin conversación → contexto vacío. El caller decide qué hacer.
            return new SystemContext();
        }

        // 2. Reutilizar BuildAsync para los sourceKeys básicos (tenant/campaign/contact).
        var ctx = await BuildAsync(
            convInfo.TenantId, convInfo.CampaignId, convInfo.ClientPhone, convInfo.Id, null, ct);

        // 3. Sobreescribir/agregar los keys específicos del resultado.
        ctx.Set("conversation.closedAt", convInfo.ClosedAt?.ToString("O"));
        ctx.Set("conversation.messageCount", convInfo.MessageCount.ToString());
        ctx.Set("conversation.closeReason", DeriveCloseReason(convInfo.Status, convInfo.IsHumanHandled));

        if (convInfo.LabelId.HasValue && !string.IsNullOrEmpty(convInfo.LabelName))
        {
            ctx.Set("conversation.label.name", convInfo.LabelName);
            ctx.Set("conversation.label.slug", Slugify(convInfo.LabelName));
            ctx.Set("conversation.label.keywords", convInfo.LabelKeywords is null ? null : string.Join(", ", convInfo.LabelKeywords));
            ctx.Set("conversation.label.labeledAt", convInfo.LabeledAt?.ToString("O"));
        }

        // 4. campaign.externalRef → primer registro del ContactDataJson con clave que
        //    parezca identificador externo (clientId / nroPoliza / cedula / externalId).
        //    Permite que el cliente correlacione la conversación con su sistema sin schema rígido.
        if (convInfo.CampaignId.HasValue)
        {
            var contactJson = await db.CampaignContacts
                .Where(cc => cc.CampaignId == convInfo.CampaignId && cc.PhoneNumber == convInfo.ClientPhone)
                .Select(cc => cc.ContactDataJson)
                .FirstOrDefaultAsync(ct);

            if (!string.IsNullOrEmpty(contactJson))
            {
                var externalRef = ExtractExternalRef(contactJson);
                if (externalRef is not null)
                {
                    ctx.Set("contact.externalId", externalRef);
                    ctx.Set("campaign.externalRef", externalRef);
                }
            }
        }

        return ctx;
    }

    private static string DeriveCloseReason(AgentFlow.Domain.Enums.ConversationStatus status, bool isHumanHandled)
    {
        return status switch
        {
            AgentFlow.Domain.Enums.ConversationStatus.Closed when isHumanHandled => "AgentClose",
            AgentFlow.Domain.Enums.ConversationStatus.Closed => "AutoClose",
            AgentFlow.Domain.Enums.ConversationStatus.EscalatedToHuman => "EscalatedClose",
            _ => "Open",
        };
    }

    private static string Slugify(string input)
    {
        var lowered = input.ToLowerInvariant().Trim();
        var sb = new System.Text.StringBuilder(lowered.Length);
        foreach (var ch in lowered)
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (ch == ' ' || ch == '-' || ch == '_') sb.Append('_');
        }
        return sb.ToString().Trim('_');
    }

    private static string? ExtractExternalRef(string contactJson)
    {
        try
        {
            var records = System.Text.Json.JsonSerializer
                .Deserialize<List<Dictionary<string, System.Text.Json.JsonElement>>>(contactJson);
            if (records is not { Count: > 0 }) return null;

            var first = records[0];
            // Heurística: probar claves comunes de identificador externo en orden de prioridad.
            string[] candidates = ["externalId", "clientId", "nroPoliza", "policyNumber", "cedula", "documentId", "keyValue"];
            foreach (var key in candidates)
            {
                if (first.TryGetValue(key, out var val))
                {
                    var s = val.ValueKind == System.Text.Json.JsonValueKind.String
                        ? val.GetString()
                        : val.ToString();
                    if (!string.IsNullOrEmpty(s)) return s;
                }
            }
        }
        catch { /* JSON inválido — ignorar */ }
        return null;
    }
}
