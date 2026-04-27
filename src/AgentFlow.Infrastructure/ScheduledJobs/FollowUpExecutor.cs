using System.Text.Json;
using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.ScheduledJobs;

/// <summary>
/// Executor del job FOLLOW_UP_MESSAGE. Cada job DelayFromEvent creado por el
/// CampaignDispatcherService al enviar el mensaje inicial llega aquí cuando el
/// delay (FollowUpHours[i] · 60) se cumple.
///
/// El ContextId tiene formato "{campaignContactId}:{followUpIndex}".
///
/// Reglas de omisión silenciosa (NO marcar como fallo):
/// 1. Conversación no existe o Status != Active (cliente respondió, escaló o cerró).
/// 2. Índice ya en CampaignContact.FollowUpsSentJson (retry idempotente).
/// 3. Campaign.IsActive = false (campaña cancelada).
/// 4. CampaignTemplate.FollowUpMessagesJson no tiene mensaje en ese índice.
/// 5. CampaignTemplate.FollowUpHours no tiene esa hora (config cambió tras el job).
/// </summary>
public class FollowUpExecutor(
    AgentFlowDbContext db,
    IChannelProviderFactory providerFactory,
    ILogger<FollowUpExecutor> log) : IScheduledJobExecutor
{
    public string Slug => "FOLLOW_UP_MESSAGE";

    public async Task<JobRunResult> ExecuteAsync(
        ScheduledWebhookJob job, ScheduledJobContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ctx.ContextId))
            return JobRunResult.Skipped("ContextId requerido (formato: 'campaignContactId:index').");

        var parts = ctx.ContextId.Split(':');
        if (parts.Length != 2 || !Guid.TryParse(parts[0], out var contactId) || !int.TryParse(parts[1], out var index))
            return JobRunResult.Skipped($"ContextId con formato inválido: '{ctx.ContextId}'.");

        var contact = await db.CampaignContacts
            .Include(c => c.Campaign)
            .FirstOrDefaultAsync(c => c.Id == contactId, ct);
        if (contact is null)
            return JobRunResult.Skipped($"CampaignContact {contactId} no existe.");

        if (!contact.Campaign.IsActive)
            return JobRunResult.Skipped("Campaña cancelada.");

        // Idempotencia: si ya enviamos este índice, omitir.
        var sent = ParseIndices(contact.FollowUpsSentJson);
        if (sent.Contains(index))
        {
            log.LogInformation("FollowUp {Idx} ya enviado a contacto {Id} — omitido.", index, contactId);
            return JobRunResult.Skipped($"Índice {index} ya enviado.");
        }

        // ── Estado de la conversación: solo enviamos si está Active ─────────
        var conv = await db.Conversations
            .Where(c => c.TenantId == contact.Campaign.TenantId
                        && c.ClientPhone == contact.PhoneNumber
                        && c.CampaignId == contact.CampaignId)
            .OrderByDescending(c => c.LastActivityAt)
            .FirstOrDefaultAsync(ct);

        if (conv is null)
        {
            log.LogInformation("Sin conversación para contacto {Id} — el primer mensaje aún no se procesó.", contactId);
            return JobRunResult.Skipped("Sin conversación activa.");
        }
        if (conv.Status != ConversationStatus.Active)
        {
            log.LogInformation("Conversación {Conv} en estado {Status} — seguimiento {Idx} omitido.",
                conv.Id, conv.Status, index);
            return JobRunResult.Skipped($"Conversación en {conv.Status}.");
        }

        // ── Resolver mensaje desde CampaignTemplate ─────────────────────────
        var template = await db.CampaignTemplates
            .FirstOrDefaultAsync(t => t.Id == contact.Campaign.CampaignTemplateId, ct);
        if (template is null)
            return JobRunResult.Skipped("CampaignTemplate no encontrado (campaña sin maestro).");

        if (string.IsNullOrEmpty(template.FollowUpMessagesJson))
            return JobRunResult.Skipped("Maestro sin FollowUpMessagesJson configurado.");

        List<string>? messages;
        try
        {
            messages = JsonSerializer.Deserialize<List<string>>(template.FollowUpMessagesJson);
        }
        catch (Exception ex)
        {
            return JobRunResult.Failed($"FollowUpMessagesJson inválido: {ex.Message}");
        }
        if (messages is null || index >= messages.Count || string.IsNullOrWhiteSpace(messages[index]))
            return JobRunResult.Skipped($"No hay mensaje en el índice {index}.");

        var rawMessage = messages[index];
        var resolved = ResolveVariables(rawMessage, contact);

        // ── Envío vía IChannelProvider del tenant ───────────────────────────
        var provider = await providerFactory.GetProviderAsync(contact.Campaign.TenantId, ct);
        if (provider is null)
        {
            log.LogWarning("Tenant {Tenant} sin canal activo — seguimiento no enviado.", contact.Campaign.TenantId);
            return JobRunResult.Failed("Tenant sin línea WhatsApp activa.");
        }

        var sendResult = await provider.SendMessageAsync(
            new SendMessageRequest(contact.PhoneNumber, resolved), ct);

        if (!sendResult.Success)
            return JobRunResult.Failed(sendResult.Error ?? "Envío falló sin detalle.");

        // ── Persistir el envío como Outbound + actualizar contact ───────────
        var outbound = new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = conv.Id,
            Direction = MessageDirection.Outbound,
            Status = MessageStatus.Sent,
            Content = resolved,
            ExternalMessageId = sendResult.ExternalMessageId,
            IsFromAgent = true,
            AgentName = "FollowUp",
            SentAt = DateTime.UtcNow
        };
        db.Messages.Add(outbound);

        sent.Add(index);
        contact.FollowUpsSentJson = JsonSerializer.Serialize(sent);
        conv.LastActivityAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        log.LogInformation("FollowUp {Idx} enviado a {Phone} (conv {Conv}).",
            index, contact.PhoneNumber, conv.Id);

        return JobRunResult.Success(1, $"Seguimiento {index + 1} enviado a {contact.PhoneNumber}.");
    }

    private static List<int> ParseIndices(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<int>();
        try { return JsonSerializer.Deserialize<List<int>>(json) ?? new List<int>(); }
        catch { return new List<int>(); }
    }

    /// <summary>
    /// Reemplaza {nombre}, {poliza}, {aseguradora}, {monto_pendiente} por los
    /// valores del contacto. Las variables sin valor se reemplazan por string vacío.
    /// </summary>
    private static string ResolveVariables(string template, CampaignContact c)
    {
        return template
            .Replace("{nombre}", c.ClientName ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{poliza}", c.PolicyNumber ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{aseguradora}", c.InsuranceCompany ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{monto_pendiente}", c.PendingAmount?.ToString("N2") ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{telefono}", c.PhoneNumber, StringComparison.OrdinalIgnoreCase);
    }
}
