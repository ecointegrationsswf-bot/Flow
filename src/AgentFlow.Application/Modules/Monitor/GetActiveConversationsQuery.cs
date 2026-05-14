using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Enums;
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
    string? LastMessagePreview,
    // === Para canal Email ===
    /// <summary>Asunto del último correo SALIENTE de la conversación (o null si no hay).
    /// Se prefiere mostrar esto en la card en lugar del HTML del body. </summary>
    string? LastEmailSubject,
    /// <summary>Cantidad de correos salientes (Direction=Outbound, Channel=Email) emitidos
    /// dentro del rango de fechas filtrado. Permite que el badge del tab Email refleje
    /// los correos enviados por día — si mañana se reenvía a los mismos contactos, suma.</summary>
    int OutboundEmailCount
);

public class GetActiveConversationsHandler(IConversationRepository repo)
    : IRequestHandler<GetActiveConversationsQuery, IEnumerable<ConversationSummary>>
{
    public async Task<IEnumerable<ConversationSummary>> Handle(
        GetActiveConversationsQuery q, CancellationToken ct)
    {
        var convs = await repo.GetActiveByTenantAsync(
            q.TenantId, q.FromUtc, q.ToUtc, q.LaunchedByUserId, ct);

        // Ventana de fechas en hora local Panamá (UTC-5, sin DST). Usamos la MISMA
        // semántica que el filtro de conversations para que los conteos cuadren.
        const int paOffsetHours = -5;
        DateTime? fromDate = q.FromUtc?.Date;
        DateTime? toDate = q.ToUtc?.Date;

        return convs.Select(c =>
        {
            var lastMsg = c.Messages.OrderByDescending(m => m.SentAt).FirstOrDefault();
            string? preview = null;
            if (lastMsg != null && !string.IsNullOrEmpty(lastMsg.Content))
                preview = lastMsg.Content[..Math.Min(80, lastMsg.Content.Length)];

            // Último correo SALIENTE de la conversación (sin filtro de fecha — siempre
            // quieres ver el más reciente, aunque sea de antes del rango).
            var lastOutboundEmail = c.Messages
                .Where(m => m.Direction == MessageDirection.Outbound
                         && m.Channel == ChannelType.Email)
                .OrderByDescending(m => m.SentAt)
                .FirstOrDefault();

            // Conteo de correos salientes dentro del rango — alineado con el filtro
            // de fecha civil PA del repo. Esto es lo que alimenta el badge del tab.
            var outboundEmailCount = c.Messages.Count(m =>
                m.Direction == MessageDirection.Outbound
                && m.Channel == ChannelType.Email
                && (!fromDate.HasValue || m.SentAt.AddHours(paOffsetHours).Date >= fromDate.Value)
                && (!toDate.HasValue   || m.SentAt.AddHours(paOffsetHours).Date <= toDate.Value));

            return new ConversationSummary(
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
                preview,
                lastOutboundEmail?.Subject,
                outboundEmailCount
            );
        });
    }
}
