using AgentFlow.Domain.Interfaces;
using MediatR;

namespace AgentFlow.Application.Modules.Monitor;

public record GetConversationDetailQuery(Guid TenantId, Guid ConversationId)
    : IRequest<ConversationDetail?>;

public record ConversationDetail(
    Guid Id,
    string ClientPhone,
    string? ClientName,
    string? PolicyNumber,
    string Status,
    string Channel,
    bool IsHumanHandled,
    DateTime LastActivityAt,
    IEnumerable<MessageDetail> Messages,
    string? CampaignName
);

public record MessageDetail(
    Guid Id,
    string Content,
    bool IsFromAgent,
    string Direction,
    DateTime SentAt,
    string? ExternalMessageId,
    string? AgentName,
    string? DetectedIntent,
    // Phase 1 outbound tracking — diferenciamos mensajes por canal en el monitor.
    // Si Channel es null, el mensaje usa el canal de la Conversation.
    string? Channel,
    string? Subject,
    string? Recipient,
    string Status,
    // Delivery status real reportado por UltraMsg vía webhook message_ack.
    // Se llena cuando el toggle "Webhook on ACK" está activo en la instancia
    // y el mensaje es saliente. Para entrantes y canales sin tracking queda NULL.
    //   queue | sent | delivered | read | invalid | failed | expired | unsent
    string? DeliveryStatus,
    int? LastAck,
    DateTime? DeliveredAt,
    DateTime? ReadAt
);

public class GetConversationDetailHandler(IConversationRepository repo)
    : IRequestHandler<GetConversationDetailQuery, ConversationDetail?>
{
    public async Task<ConversationDetail?> Handle(
        GetConversationDetailQuery q, CancellationToken ct)
    {
        var conv = await repo.GetByIdAsync(q.ConversationId, ct);

        // Verificar que pertenece al tenant (seguridad multitenante)
        if (conv is null || conv.TenantId != q.TenantId)
            return null;

        // Obtener nombre de campaña usando el repositorio existente
        string? campaignName = null;
        if (conv.CampaignId.HasValue)
        {
            var campaign = await repo.GetCampaignAsync(conv.CampaignId.Value, ct);
            campaignName = campaign?.Name;
        }

        return new ConversationDetail(
            conv.Id,
            conv.ClientPhone,
            conv.ClientName,
            conv.PolicyNumber,
            conv.Status.ToString(),
            conv.Channel.ToString(),
            conv.IsHumanHandled,
            conv.LastActivityAt,
            conv.Messages
                .OrderBy(m => m.SentAt)
                .Select(m => new MessageDetail(
                    m.Id,
                    m.Content,
                    m.IsFromAgent,
                    m.Direction.ToString(),
                    m.SentAt,
                    m.ExternalMessageId,
                    m.AgentName,
                    m.DetectedIntent,
                    m.Channel?.ToString(),
                    m.Subject,
                    m.Recipient,
                    m.Status.ToString(),
                    m.DeliveryStatus,
                    m.LastAck,
                    m.DeliveredAt,
                    m.ReadAt
                )),
            campaignName
        );
    }
}
