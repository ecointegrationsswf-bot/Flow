using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.Infrastructure.Campaigns;

/// <summary>
/// Cuando un cliente indica que quiere hablar con un humano ([INTENT:humano]),
/// y la campaña tiene vinculada la acción TRANSFER_CHAT, este servicio envía
/// un mensaje de WhatsApp al ejecutivo que lanzó la campaña con el resumen
/// de la conversación.
/// </summary>
public class TransferChatService(
    AgentFlowDbContext db,
    IChannelProviderFactory channelFactory) : ITransferChatService
{
    private const string ActionName = "TRANSFER_CHAT";
    private const int SummaryMessageCount = 10;

    public async Task ExecuteIfApplicableAsync(Conversation conversation, CancellationToken ct = default)
    {
        // Solo aplica si la conversación viene de una campaña
        if (!conversation.CampaignId.HasValue) return;

        // Cargar campaña con template
        var campaign = await db.Campaigns
            .Include(c => c.CampaignTemplate)
            .FirstOrDefaultAsync(c => c.Id == conversation.CampaignId.Value, ct);

        if (campaign is null) return;

        // Obtener teléfono de notificación: primero el guardado en la campaña,
        // si está vacío buscar el actual en el perfil del ejecutivo por email (fallback
        // para campañas lanzadas antes de que el ejecutivo configurara su número).
        var notifyPhone = campaign.LaunchedByUserPhone;
        if (string.IsNullOrEmpty(notifyPhone) && !string.IsNullOrEmpty(campaign.LaunchedByUserId))
        {
            var launcher = await db.AppUsers
                .Where(u => u.TenantId == conversation.TenantId
                         && (u.Email == campaign.LaunchedByUserId || u.FullName == campaign.LaunchedByUserId))
                .FirstOrDefaultAsync(ct);

            notifyPhone = launcher?.NotifyPhone;

            // Actualizar la campaña para que próximas escalaciones no necesiten hacer el lookup
            if (!string.IsNullOrEmpty(notifyPhone))
            {
                campaign.LaunchedByUserPhone = notifyPhone;
                await db.SaveChangesAsync(ct);
            }
        }

        if (string.IsNullOrEmpty(notifyPhone)) return;

        // Verificar que el maestro tenga vinculada la acción TRANSFER_CHAT
        if (campaign.CampaignTemplate is null || campaign.CampaignTemplate.ActionIds.Count == 0)
            return;

        var hasTransferAction = await db.ActionDefinitions
            .AnyAsync(a => campaign.CampaignTemplate.ActionIds.Contains(a.Id)
                        && a.Name == ActionName
                        && a.IsActive, ct);

        if (!hasTransferAction) return;

        // Obtener los últimos mensajes para el resumen
        var recentMessages = await db.Messages
            .Where(m => m.ConversationId == conversation.Id)
            .OrderByDescending(m => m.SentAt)
            .Take(SummaryMessageCount)
            .OrderBy(m => m.SentAt)
            .ToListAsync(ct);

        var summary = BuildSummary(conversation, recentMessages);

        // Enviar WhatsApp al ejecutivo
        try
        {
            var provider = await channelFactory.GetProviderAsync(conversation.TenantId, ct);
            if (provider is null)
            {
                Console.WriteLine("[TransferChat] No hay proveedor WhatsApp disponible para el tenant.");
                return;
            }

            await provider.SendMessageAsync(
                new SendMessageRequest(notifyPhone, summary), ct);

            Console.WriteLine($"[TransferChat] Notificación enviada a {campaign.LaunchedByUserPhone} " +
                              $"— conversación {conversation.Id}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TransferChat] Error enviando notificación: {ex.Message}");
        }
    }

    private static string BuildSummary(Conversation conversation, List<Message> messages)
    {
        var clientName = conversation.ClientName ?? conversation.ClientPhone;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("⚠️ *Solicitud de atención humana*");
        sb.AppendLine($"Cliente: {clientName}");
        sb.AppendLine($"Teléfono: {conversation.ClientPhone}");
        sb.AppendLine();

        if (messages.Count > 0)
        {
            sb.AppendLine("*Últimos mensajes:*");
            foreach (var m in messages)
            {
                var who = m.IsFromAgent ? "Agente" : "Cliente";
                var text = m.Content.Length > 120
                    ? m.Content[..120] + "..."
                    : m.Content;
                sb.AppendLine($"• {who}: {text}");
            }
            sb.AppendLine();
        }

        sb.Append("El cliente solicitó hablar directamente con un ejecutivo.");
        return sb.ToString();
    }
}
