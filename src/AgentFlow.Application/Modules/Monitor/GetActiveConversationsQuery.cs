using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Interfaces;
using MediatR;

namespace AgentFlow.Application.Modules.Monitor;

/// <summary>
/// Filtros opcionales:
///   FromUtc/ToUtc — filtra por LastActivityAt en ese rango (UTC).
///   LaunchedByUserId — sólo conversaciones cuya campaña fue lanzada por ese usuario.
///     Las conversaciones SIN CampaignId (chats orgánicos / no-campaña) se incluyen
///     siempre, sin importar el filtro de usuario.
/// </summary>
public record GetActiveConversationsQuery(
    Guid TenantId,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    string? LaunchedByUserId = null
) : IRequest<IEnumerable<ConversationSummary>>;

public record ConversationSummary(
    Guid Id,
    string ClientPhone,
    string? ClientName,
    string? PolicyNumber,
    string AgentType,
    string? AgentName,
    string Status,
    string Channel,
    bool IsHumanHandled,
    DateTime LastActivityAt,
    string? LastMessagePreview
);

public class GetActiveConversationsHandler(IConversationRepository repo)
    : IRequestHandler<GetActiveConversationsQuery, IEnumerable<ConversationSummary>>
{
    public async Task<IEnumerable<ConversationSummary>> Handle(
        GetActiveConversationsQuery q, CancellationToken ct)
    {
        var convs = await repo.GetActiveByTenantAsync(
            q.TenantId, q.FromUtc, q.ToUtc, q.LaunchedByUserId, ct);
        return convs.Select(c => new ConversationSummary(
            c.Id,
            c.ClientPhone,
            c.ClientName,
            c.PolicyNumber,
            c.ActiveAgent?.Type.ToString() ?? "unknown",
            c.ActiveAgent?.Name,
            c.Status.ToString(),
            c.Channel.ToString(),
            c.IsHumanHandled,
            c.LastActivityAt,
            c.Messages.OrderByDescending(m => m.SentAt).FirstOrDefault()?.Content[..Math.Min(80,
                c.Messages.OrderByDescending(m => m.SentAt).FirstOrDefault()?.Content.Length ?? 0)]
        ));
    }
}
