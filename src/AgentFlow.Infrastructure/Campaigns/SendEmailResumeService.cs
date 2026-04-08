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
    IEmailService emailService) : ISendEmailResumeService
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

        await emailService.SendConversationResumeAsync(
            toEmail,
            ccEmail,
            clientName,
            conversation.ClientPhone,
            policyNumber,
            lines,
            ct);

        Console.WriteLine($"[SendEmailResume] Email enviado a {toEmail}{(ccEmail is not null ? $" (CC: {ccEmail})" : "")} — conversación {conversation.Id}");
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
