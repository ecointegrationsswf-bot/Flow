using System.Text;
using System.Text.Json;
using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Email;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.Infrastructure.Campaigns;

/// <summary>
/// Cuando el agente cierra una conversación exitosamente ([INTENT:cierre])
/// y el maestro de campaña tiene vinculada la acción SEND_EMAIL_RESUME,
/// envía un email con el resumen de la gestión al email configurado en la acción.
/// </summary>
public class SendEmailResumeService(
    AgentFlowDbContext db,
    IEmailService emailService,
    EmailTemplateRenderer templateRenderer) : ISendEmailResumeService
{
    private const string ActionName = "SEND_EMAIL_RESUME";
    private const int MessageCount = 10;

    public async Task ExecuteIfApplicableAsync(Conversation conversation, CancellationToken ct = default)
    {
        Console.WriteLine($"[SendEmailResume] Iniciando — ConvId={conversation.Id} CampaignId={conversation.CampaignId}");

        // Solo aplica si la conversación viene de una campaña
        if (!conversation.CampaignId.HasValue)
        {
            Console.WriteLine("[SendEmailResume] SKIP: CampaignId es null");
            return;
        }

        var campaign = await db.Campaigns
            .Include(c => c.CampaignTemplate)
            .FirstOrDefaultAsync(c => c.Id == conversation.CampaignId.Value, ct);

        if (campaign?.CampaignTemplate is null)
        {
            Console.WriteLine($"[SendEmailResume] SKIP: Campaign={campaign?.Id} no tiene CampaignTemplate");
            return;
        }

        Console.WriteLine($"[SendEmailResume] Template={campaign.CampaignTemplate.Id} ActionIds={string.Join(",", campaign.CampaignTemplate.ActionIds)}");

        if (campaign.CampaignTemplate.ActionIds.Count == 0)
        {
            Console.WriteLine("[SendEmailResume] SKIP: Template sin acciones vinculadas");
            return;
        }

        // Verificar que la acción SEND_EMAIL_RESUME esté vinculada y activa.
        // IMPORTANTE: materializar ActionIds antes del query EF (columna JSON).
        var actionIds = campaign.CampaignTemplate.ActionIds.ToList();
        var action = await db.ActionDefinitions
            .Where(a => actionIds.Contains(a.Id) && a.Name == ActionName && a.IsActive)
            .Select(a => new { a.Id })
            .FirstOrDefaultAsync(ct);

        if (action is null)
        {
            Console.WriteLine($"[SendEmailResume] SKIP: No se encontró acción activa '{ActionName}' en los ActionIds del template");
            return;
        }

        Console.WriteLine($"[SendEmailResume] Acción encontrada Id={action.Id}");

        // Obtener el email del ejecutivo desde ActionConfigs (irá en CC)
        var executiveEmail = GetEmailFromConfigs(campaign.CampaignTemplate.ActionConfigs, action.Id);
        Console.WriteLine($"[SendEmailResume] ActionConfigs raw={campaign.CampaignTemplate.ActionConfigs}");
        Console.WriteLine($"[SendEmailResume] ExecutiveEmail={executiveEmail}");

        // Obtener el email y número de póliza del cliente desde el archivo de campaña (CampaignContact)
        var contact = await db.CampaignContacts
            .Where(c => c.CampaignId == campaign.Id && c.PhoneNumber == conversation.ClientPhone)
            .Select(c => new { c.Email, c.PolicyNumber })
            .FirstOrDefaultAsync(ct);

        var clientEmail = contact?.Email;
        // Usar póliza de la conversación o, si no está, del contacto de campaña
        var policyNumber = !string.IsNullOrEmpty(conversation.PolicyNumber)
            ? conversation.PolicyNumber
            : contact?.PolicyNumber;
        Console.WriteLine($"[SendEmailResume] ClientPhone={conversation.ClientPhone} ContactFound={contact is not null} ClientEmail={clientEmail} PolicyNumber={policyNumber}");

        // Si no hay email del cliente ni del ejecutivo, no hay a quién enviar
        if (string.IsNullOrEmpty(clientEmail) && string.IsNullOrEmpty(executiveEmail))
        {
            Console.WriteLine("[SendEmailResume] SKIP: No hay emails configurados (ni cliente ni ejecutivo)");
            return;
        }

        // Si no hay email del cliente, enviamos solo al ejecutivo (sin CC)
        var toEmail = !string.IsNullOrEmpty(clientEmail) ? clientEmail : executiveEmail!;
        var ccEmail = !string.IsNullOrEmpty(clientEmail) ? executiveEmail : null;

        // Cargar mensajes de HOY (en zona horaria de Panamá) para el resumen
        var panamaZone = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "SA Pacific Standard Time" : "America/Panama");
        var todayPanamaStart = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, panamaZone).Date, DateTimeKind.Unspecified),
            panamaZone);

        var recentMessages = await db.Messages
            .Where(m => m.ConversationId == conversation.Id && m.SentAt >= todayPanamaStart)
            .OrderBy(m => m.SentAt)
            .ToListAsync(ct);

        var lines = recentMessages
            .Select(m => (Who: m.IsFromAgent ? "Agente" : "Cliente", Text: TrimContent(m.Content)))
            .ToList();

        var clientName = conversation.ClientName ?? conversation.ClientPhone;

        // ── Plantilla personalizada (opt-in estricto) ──
        // El correo se envía SÓLO si el maestro tiene EmailBodyHtml configurado.
        // Si no tiene template, no se envía nada (la acción se considera "no aplicable").
        // Decisión de producto: cada campaña arma su propio correo; no hay default
        // genérico del sistema.
        var template = campaign.CampaignTemplate;
        if (string.IsNullOrWhiteSpace(template.EmailBodyHtml))
        {
            Console.WriteLine($"[SendEmailResume] SKIP: Maestro {template.Id} no tiene EmailBodyHtml configurado. " +
                              "Sin template no se envía correo.");
            return;
        }

        var contact2 = await db.CampaignContacts
            .Where(c => c.CampaignId == campaign.Id && c.PhoneNumber == conversation.ClientPhone)
            .Select(c => new
            {
                c.ClientName, c.PhoneNumber, c.Email, c.PolicyNumber,
                c.InsuranceCompany, c.PendingAmount, c.ContactDataJson
            })
            .FirstOrDefaultAsync(ct);

        var agentName = await db.AgentDefinitions
            .Where(a => conversation.ActiveAgentId.HasValue && a.Id == conversation.ActiveAgentId.Value)
            .Select(a => (string?)(a.AvatarName ?? a.Name))
            .FirstOrDefaultAsync(ct);

        var tenantInfo = await db.Tenants
            .Where(t => t.Id == campaign.TenantId)
            .Select(t => new { t.Name, t.LogoUrl })
            .FirstOrDefaultAsync(ct);

        var renderCtx = new EmailRenderContext
        {
            ClienteNombre      = contact2?.ClientName ?? clientName,
            ClienteTelefono    = contact2?.PhoneNumber ?? conversation.ClientPhone,
            ClienteEmail       = contact2?.Email,
            ClientePoliza      = policyNumber,
            ClienteAseguradora = contact2?.InsuranceCompany,
            ClienteSaldo       = contact2?.PendingAmount?.ToString("C2"),
            ClienteDatosJson   = contact2?.ContactDataJson,

            ConversacionResumen      = BuildResumenPlano(lines),
            ConversacionMensajesHtml = BuildResumenHtml(lines),
            ConversacionEstado       = conversation.Status.ToString(),

            CampanaNombre = campaign.Name,
            AgenteNombre  = agentName,
            TenantNombre  = tenantInfo?.Name,
            TenantLogoUrl = tenantInfo?.LogoUrl,

            Fecha = DateTime.UtcNow.ToString("dd/MM/yyyy"),
            Hora  = DateTime.UtcNow.ToString("HH:mm"),

            ItemsConfigJson   = template.ItemsConfig,
            UmbralCorporativo = template.UmbralCorporativo,
        };

        var subjectTemplate = !string.IsNullOrWhiteSpace(template.EmailSubject)
            ? template.EmailSubject
            : $"Resumen de gestión - {{cliente.nombre}}";

        var rendered = templateRenderer.Render(
            subjectTemplate, template.EmailBodyHtml, template.EmailBodyText, renderCtx);

        string? externalId = null;
        MessageStatus deliveryStatus = MessageStatus.Sent;
        string? sendError = null;
        try
        {
            externalId = await emailService.SendCustomHtmlAsync(
                toEmail, ccEmail,
                rendered.Subject, rendered.HtmlBody, rendered.TextBody,
                ct);
            Console.WriteLine($"[SendEmailResume] Email enviado a {toEmail}{(ccEmail is not null ? $" CC={ccEmail}" : "")} conv={conversation.Id} extId={externalId ?? "(none)"}");
        }
        catch (Exception ex)
        {
            // Aún así persistimos el Message con Status=Failed para que quede
            // rastro en monitor/dashboard de que la acción se intentó pero falló.
            deliveryStatus = MessageStatus.Failed;
            sendError = ex.Message;
            Console.WriteLine($"[SendEmailResume] FAIL enviando a {toEmail}: {ex.Message}");
        }

        // ── Phase 1 outbound tracking ────────────────────────────────────────
        // Persistimos el envío como un Message en la conversación, con Channel=Email.
        // Eso permite verlo en el monitor unificado y agregar KPIs en el dashboard.
        // Si más adelante recibimos webhooks de Resend (delivered/bounced/opened),
        // matcheamos por ExternalMessageId y actualizamos Status + agregamos eventos.
        var recipientLabel = !string.IsNullOrWhiteSpace(ccEmail)
            ? $"to={toEmail} cc={ccEmail}"
            : $"to={toEmail}";
        var contentForMonitor = string.IsNullOrWhiteSpace(rendered.TextBody)
            ? rendered.HtmlBody
            : rendered.TextBody;

        db.Messages.Add(new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = conversation.Id,
            Direction = MessageDirection.Outbound,
            Channel = AgentFlow.Domain.Enums.ChannelType.Email,
            Status = deliveryStatus,
            Content = contentForMonitor ?? string.Empty,
            Subject = rendered.Subject,
            Recipient = recipientLabel,
            ExternalMessageId = externalId,
            IsFromAgent = true,
            AgentName = agentName,
            SentAt = DateTime.UtcNow,
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SendEmailResume] WARN: no se pudo persistir el Message del email — {ex.Message}");
            // No re-throw: el correo ya se mandó (o falló), no queremos romper la
            // gestión por un problema persistiendo el registro.
        }

        // Si el envío falló, propagamos para que el caller pueda registrarlo.
        if (deliveryStatus == MessageStatus.Failed)
            throw new InvalidOperationException($"No se pudo enviar el correo: {sendError}");
    }

    /// <summary>Convierte el historial en un texto plano: "Cliente: hola / Agente: ...".</summary>
    private static string BuildResumenPlano(List<(string Who, string Text)> lines)
    {
        var sb = new StringBuilder();
        foreach (var (who, text) in lines)
            sb.Append(who).Append(": ").AppendLine(text);
        return sb.ToString().TrimEnd();
    }

    /// <summary>Genera bloques HTML para inyectar {{conversacion.mensajes}} en la plantilla.</summary>
    private static string BuildResumenHtml(List<(string Who, string Text)> lines)
    {
        var sb = new StringBuilder();
        foreach (var (who, text) in lines)
        {
            var bg = who == "Agente" ? "#eff6ff" : "#f8fafc";
            var label = who == "Agente" ? "Agente IA" : "Cliente";
            sb.Append($"<div style=\"background:{bg};padding:8px 12px;margin:4px 0;border-radius:6px;\">")
              .Append($"<strong style=\"font-size:11px;color:#475569;\">{label}</strong><br>")
              .Append(System.Net.WebUtility.HtmlEncode(text))
              .Append("</div>");
        }
        return sb.ToString();
    }

    /// <summary>Extrae el emailAddress del JSON de configuraciones de acciones del template.</summary>
    private static string? GetEmailFromConfigs(string? actionConfigs, Guid actionId)
    {
        if (string.IsNullOrEmpty(actionConfigs)) return null;

        try
        {
            var configs = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(actionConfigs);
            if (configs is null) return null;

            var key = actionId.ToString();
            if (!configs.TryGetValue(key, out var cfg)) return null;

            return cfg.TryGetProperty("emailAddress", out var emailProp)
                ? emailProp.GetString()
                : null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SendEmailResume] Error leyendo ActionConfigs: {ex.Message}");
            return null;
        }
    }

    /// <summary>Recorta el contenido de un mensaje y elimina el tag [media:...] si lo tiene.</summary>
    private static string TrimContent(string content)
    {
        var idx = content.IndexOf("\n[media:", StringComparison.Ordinal);
        var clean = idx >= 0 ? content[..idx].Trim() : content;
        return clean.Length > 200 ? clean[..200] + "..." : clean;
    }
}
