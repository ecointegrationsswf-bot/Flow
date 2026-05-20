using System.Net;
using System.Text;
using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Email;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.Campaigns;

/// <summary>
/// Cuando un cliente solicita atención humana (vía [INTENT:humano], canned keyword
/// o agentResponse.ShouldEscalate), este servicio notifica al equipo del tenant.
///
/// Reglas (configurables — ver Notion / análisis de Mayo 2026):
///   • Si la campaña fue lanzada por un usuario humano (Campaign.LaunchedByUserId
///     es email/Id real) → notifica SOLO a ese ejecutivo por WhatsApp + Email.
///   • Si la campaña es automática (LaunchedByUserId == "system" o NULL) → fan-out
///     a TODOS los AppUsers con IsActive=1 del tenant, por WhatsApp + Email.
///
/// Cooldown: 1 notificación por conversación. Conversation.LastTransferChatSentAt
/// es el marcador. Si != NULL → no se vuelve a notificar (el ejecutivo gestiona
/// desde el portal). Esto evita spam si el cliente sigue escribiendo después de
/// escalar y el agente vuelve a marcar EscalateToHuman.
///
/// Requisitos para que aplique:
///   1. La conversación pertenece a una campaña (CampaignId != NULL).
///   2. El template de la campaña tiene TRANSFER_CHAT en ActionIds.
///   3. Cooldown NO disparado (LastTransferChatSentAt es NULL).
/// </summary>
public class TransferChatService(
    AgentFlowDbContext db,
    IChannelProviderFactory channelFactory,
    IEmailService emailService,
    ILogger<TransferChatService> log) : ITransferChatService
{
    private const string ActionName = "TRANSFER_CHAT";
    private const int SummaryMessageCount = 10;

    public async Task ExecuteIfApplicableAsync(Conversation conversation, CancellationToken ct = default)
    {
        // 1) Solo aplica a conversaciones de campaña.
        if (!conversation.CampaignId.HasValue) return;

        // 2) Cooldown: 1 notificación por conversación.
        if (conversation.LastTransferChatSentAt.HasValue)
        {
            log.LogInformation("[TransferChat] Conversación {Id} ya notificada el {When} — skip.",
                conversation.Id, conversation.LastTransferChatSentAt.Value);
            return;
        }

        // 3) Cargar campaña + template.
        var campaign = await db.Campaigns
            .Include(c => c.CampaignTemplate)
            .FirstOrDefaultAsync(c => c.Id == conversation.CampaignId.Value, ct);
        if (campaign is null) return;

        // 4) Verificar que el template tenga TRANSFER_CHAT vinculada.
        if (campaign.CampaignTemplate is null || campaign.CampaignTemplate.ActionIds.Count == 0)
        {
            log.LogDebug("[TransferChat] Template de campaña {CampaignId} sin ActionIds — skip.", campaign.Id);
            return;
        }
        var actionIds = campaign.CampaignTemplate.ActionIds.ToList();
        var hasTransferAction = await db.ActionDefinitions
            .AnyAsync(a => actionIds.Contains(a.Id) && a.Name == ActionName && a.IsActive, ct);
        if (!hasTransferAction)
        {
            log.LogDebug("[TransferChat] Template {TemplateId} no incluye TRANSFER_CHAT — skip.",
                campaign.CampaignTemplate.Id);
            return;
        }

        // 5) Resolver destinatarios según si la campaña es manual o automática.
        var isAutomatic = string.IsNullOrEmpty(campaign.LaunchedByUserId)
                          || string.Equals(campaign.LaunchedByUserId, "system", StringComparison.OrdinalIgnoreCase);

        var recipients = isAutomatic
            ? await ResolveAllActiveUsersAsync(conversation.TenantId, ct)
            : await ResolveLauncherAsync(conversation.TenantId, campaign, ct);

        if (recipients.Count == 0)
        {
            log.LogWarning("[TransferChat] Sin destinatarios para conversación {Id} (campaña {CampaignId}, automatic={Auto}). " +
                           "Verificar AppUsers IsActive=1 del tenant o LaunchedByUserId.",
                conversation.Id, campaign.Id, isAutomatic);
            return;
        }

        // 6) Cargar últimos mensajes para el resumen.
        var recentMessages = await db.Messages
            .Where(m => m.ConversationId == conversation.Id)
            .OrderByDescending(m => m.SentAt)
            .Take(SummaryMessageCount)
            .OrderBy(m => m.SentAt)
            .ToListAsync(ct);

        // 7) Obtener tenant para subject/branding del email.
        var tenant = await db.Tenants
            .Where(t => t.Id == conversation.TenantId)
            .Select(t => new { t.Name })
            .FirstOrDefaultAsync(ct);
        var tenantName = tenant?.Name ?? "Tu cuenta";

        // 8) Enviar WhatsApp + email a cada destinatario.
        var waSummary = BuildWhatsAppSummary(conversation, campaign, recentMessages, tenantName, isAutomatic);
        var (emailSubject, emailHtml) = BuildEmailHtml(conversation, campaign, recentMessages, tenantName, isAutomatic);

        IChannelProvider? waProvider = null;
        try { waProvider = await channelFactory.GetProviderAsync(conversation.TenantId, ct); }
        catch (Exception ex) { log.LogWarning(ex, "[TransferChat] No se pudo obtener proveedor WhatsApp para tenant {TenantId}.", conversation.TenantId); }

        var notifiedWa = 0;
        var notifiedEmail = 0;

        foreach (var r in recipients)
        {
            if (ct.IsCancellationRequested) break;

            // WhatsApp si tiene NotifyPhone y hay proveedor disponible.
            if (waProvider is not null && !string.IsNullOrWhiteSpace(r.NotifyPhone))
            {
                try
                {
                    await waProvider.SendMessageAsync(new SendMessageRequest(r.NotifyPhone, waSummary), ct);
                    notifiedWa++;
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "[TransferChat] Falló WhatsApp a {Phone} (user {Email}).", r.NotifyPhone, r.Email);
                }
            }

            // Email si tiene Email.
            if (!string.IsNullOrWhiteSpace(r.Email))
            {
                try
                {
                    await emailService.SendCustomHtmlAsync(r.Email, ccEmail: null, emailSubject, emailHtml, textBody: null, ct);
                    notifiedEmail++;
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "[TransferChat] Falló email a {Email}.", r.Email);
                }
            }
        }

        // 9) Marcar cooldown (aunque haya fallado todo — para no spamear reintentos).
        conversation.LastTransferChatSentAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        log.LogInformation(
            "[TransferChat] Conversación {Id} ({Client}) notificada: WA={Wa} Email={Em} (recipients={Total} · automatic={Auto})",
            conversation.Id, conversation.ClientName ?? conversation.ClientPhone,
            notifiedWa, notifiedEmail, recipients.Count, isAutomatic);
    }

    private record RecipientRow(string? Email, string? NotifyPhone, string? FullName);

    private async Task<List<RecipientRow>> ResolveLauncherAsync(Guid tenantId, Campaign campaign, CancellationToken ct)
    {
        // El campo LaunchedByUserId puede contener email, nombre completo o Id.
        // Buscamos por email primero (lo más común desde el frontend), luego nombre.
        var launcherKey = campaign.LaunchedByUserId;
        var launcher = await db.AppUsers
            .Where(u => u.TenantId == tenantId && u.IsActive
                     && (u.Email == launcherKey || u.FullName == launcherKey))
            .Select(u => new RecipientRow(u.Email, u.NotifyPhone, u.FullName))
            .FirstOrDefaultAsync(ct);

        if (launcher is not null) return new List<RecipientRow> { launcher };

        // Si no resolvió, fallback a "todos los usuarios activos" (tratamos como automática).
        log.LogWarning("[TransferChat] LaunchedByUserId='{Launcher}' no resolvió a un AppUser activo de tenant {TenantId}. " +
                       "Fallback: broadcast a todos los usuarios activos.",
            launcherKey, tenantId);
        return await ResolveAllActiveUsersAsync(tenantId, ct);
    }

    private async Task<List<RecipientRow>> ResolveAllActiveUsersAsync(Guid tenantId, CancellationToken ct)
    {
        return await db.AppUsers
            .Where(u => u.TenantId == tenantId && u.IsActive)
            .Select(u => new RecipientRow(u.Email, u.NotifyPhone, u.FullName))
            .ToListAsync(ct);
    }

    private static string BuildWhatsAppSummary(
        Conversation conversation, Campaign campaign, List<Message> messages,
        string tenantName, bool isAutomatic)
    {
        var clientName = conversation.ClientName ?? conversation.ClientPhone;
        var sb = new StringBuilder();
        sb.AppendLine($"⚠️ *Solicitud de atención humana — {tenantName}*");
        sb.AppendLine();
        sb.AppendLine($"*Cliente:* {clientName}");
        sb.AppendLine($"*Teléfono:* {conversation.ClientPhone}");
        sb.AppendLine($"*Campaña:* {campaign.Name}");
        if (isAutomatic)
            sb.AppendLine("*Origen:* Campaña automática");
        sb.AppendLine();

        if (messages.Count > 0)
        {
            sb.AppendLine("*Últimos mensajes:*");
            foreach (var m in messages)
            {
                var who = m.IsFromAgent ? "Agente" : "Cliente";
                var text = m.Content.Length > 120 ? m.Content[..120] + "..." : m.Content;
                sb.AppendLine($"• {who}: {text}");
            }
            sb.AppendLine();
        }

        sb.Append("El cliente solicitó atención humana. Ingresa al portal para tomar la conversación.");
        return sb.ToString();
    }

    private static (string Subject, string Html) BuildEmailHtml(
        Conversation conversation, Campaign campaign, List<Message> messages,
        string tenantName, bool isAutomatic)
    {
        var clientName = conversation.ClientName ?? conversation.ClientPhone;
        var subject = $"⚠️ Cliente solicita atención humana — {clientName} ({tenantName})";

        var sb = new StringBuilder();
        sb.Append("""
<!DOCTYPE html>
<html>
<head><meta charset="UTF-8"></head>
<body style="font-family:Arial,Helvetica,sans-serif;background:#f4f6f8;margin:0;padding:24px;color:#1f2937;">
<table cellpadding="0" cellspacing="0" border="0" width="100%" style="max-width:640px;margin:0 auto;background:#ffffff;border-radius:8px;overflow:hidden;box-shadow:0 2px 8px rgba(0,0,0,.08);">
""");

        // Header
        sb.Append("""
<tr><td bgcolor="#1a3a6b" style="background:#1a3a6b;color:#ffffff;padding:24px;">
  <h1 style="margin:0;font-size:22px;color:#ffffff;">⚠️ Solicitud de atención humana</h1>
""");
        sb.Append($"<p style=\"margin:8px 0 0;color:#cfd8e3;font-size:14px;\">{HtmlEscape(tenantName)} · {DateTime.UtcNow.AddHours(-5):dd-MMM-yyyy HH:mm} hora Panamá</p>");
        sb.Append("</td></tr>");

        // Intro
        sb.Append("<tr><td style=\"padding:24px;\">");
        sb.Append($"<p style=\"margin:0 0 16px;font-size:15px;\">Un cliente solicitó hablar con un ejecutivo. Esta es la información de contexto:</p>");

        // Datos del cliente
        sb.Append("""
<div style="background:#f0f9ff;border-left:4px solid #1a3a6b;border-radius:4px;padding:16px;margin:0 0 20px;">
  <table cellpadding="4" cellspacing="0" border="0" style="font-size:14px;width:100%;">
""");
        sb.Append($"<tr><td style=\"color:#6b7280;width:130px;\">Cliente:</td><td><b>{HtmlEscape(clientName ?? "(sin nombre)")}</b></td></tr>");
        sb.Append($"<tr><td style=\"color:#6b7280;\">Teléfono:</td><td><b>{HtmlEscape(conversation.ClientPhone)}</b></td></tr>");
        sb.Append($"<tr><td style=\"color:#6b7280;\">Campaña:</td><td>{HtmlEscape(campaign.Name)}</td></tr>");
        sb.Append($"<tr><td style=\"color:#6b7280;\">Origen:</td><td>{(isAutomatic ? "<span style=\"color:#92400e;\">Campaña automática</span>" : $"Manual ({HtmlEscape(campaign.LaunchedByUserId ?? "—")})")}</td></tr>");
        if (!string.IsNullOrEmpty(conversation.PolicyNumber))
            sb.Append($"<tr><td style=\"color:#6b7280;\">Póliza:</td><td>{HtmlEscape(conversation.PolicyNumber)}</td></tr>");
        sb.Append("</table></div>");

        // Resumen de la conversación
        if (messages.Count > 0)
        {
            sb.Append("<h3 style=\"margin:24px 0 12px;color:#1a3a6b;font-size:16px;\">Últimos mensajes de la conversación</h3>");
            sb.Append("<table cellpadding=\"0\" cellspacing=\"0\" border=\"0\" width=\"100%\" style=\"border-collapse:collapse;font-size:13px;\">");
            foreach (var m in messages)
            {
                var isAgent = m.IsFromAgent;
                var bg = isAgent ? "#e0f2fe" : "#f3f4f6";
                var label = isAgent ? "Agente IA" : "Cliente";
                var labelColor = isAgent ? "#0c4a6e" : "#374151";
                var when = m.SentAt.AddHours(-5).ToString("HH:mm");
                var content = HtmlEscape(m.Content).Replace("\n", "<br>");
                sb.Append($"<tr><td style=\"padding:8px 0;\"><table cellpadding=\"10\" cellspacing=\"0\" border=\"0\" style=\"background:{bg};border-radius:6px;width:100%;\"><tr><td>");
                sb.Append($"<div style=\"font-size:11px;color:{labelColor};font-weight:bold;text-transform:uppercase;letter-spacing:.5px;margin-bottom:4px;\">{label} · {when}</div>");
                sb.Append($"<div style=\"color:#1f2937;line-height:1.5;\">{content}</div>");
                sb.Append("</td></tr></table></td></tr>");
            }
            sb.Append("</table>");
        }

        // CTA
        sb.Append("""
<div style="margin:28px 0 8px;text-align:center;">
  <p style="margin:0 0 12px;color:#4b5563;font-size:14px;">Ingresa al portal para tomar la conversación y atender al cliente:</p>
  <a href="http://jamconsulting-004-site11.site4future.com/monitor" style="display:inline-block;background:#1a3a6b;color:#ffffff;text-decoration:none;padding:12px 28px;border-radius:6px;font-weight:bold;font-size:14px;">Ir al monitor</a>
</div>
""");

        // Footer
        sb.Append("""
<p style="margin:32px 0 0;color:#9ca3af;font-size:12px;text-align:center;">
  Este aviso se envía una sola vez por conversación. Si el cliente sigue
  escribiendo después de escalar, no se reenviará — gestiona desde el portal.
</p>
</td></tr></table>
</body></html>
""");

        return (subject, sb.ToString());
    }

    private static string HtmlEscape(string? s) =>
        string.IsNullOrEmpty(s) ? "" : WebUtility.HtmlEncode(s);
}
