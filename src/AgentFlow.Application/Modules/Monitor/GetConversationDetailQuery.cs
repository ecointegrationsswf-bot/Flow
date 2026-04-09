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
    string? DetectedIntent
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
                    m.DetectedIntent
                )),
            conv.Campaign?.Name
        );
    }
}
