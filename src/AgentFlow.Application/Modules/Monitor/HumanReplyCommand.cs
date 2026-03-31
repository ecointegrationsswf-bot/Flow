using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;
using MediatR;

namespace AgentFlow.Application.Modules.Monitor;

/// <summary>
/// Un ejecutivo envía un mensaje directo al cliente desde el monitor.
/// Guarda el mensaje en BD y lo envía por WhatsApp.
/// </summary>
public record HumanReplyCommand(
    Guid TenantId,
    Guid ConversationId,
    string Message,
    string? SentByUserId
) : IRequest<HumanReplyResult>;

public record HumanReplyResult(
    Guid MessageId,
    bool WasSentToWhatsApp,
    string Content,
    DateTime SentAt
);

public class HumanReplyHandler(
    IConversationRepository conversations,
    IChannelProviderFactory channelFactory,
    IConversationNotifier notifier
) : IRequestHandler<HumanReplyCommand, HumanReplyResult>
{
    public async Task<HumanReplyResult> Handle(HumanReplyCommand cmd, CancellationToken ct)
    {
        var conv = await conversations.GetByIdAsync(cmd.ConversationId, ct);
        if (conv is null || conv.TenantId != cmd.TenantId)
            throw new InvalidOperationException("Conversación no encontrada");

        // ── Guardar mensaje del ejecutivo ─────────────────
        var message = new Message
        {
            Id        = Guid.NewGuid(),
            ConversationId = conv.Id,
            Direction = MessageDirection.Outbound,
            Status    = MessageStatus.Sent,
            Content   = cmd.Message,
            IsFromAgent = false,
            AgentName = "Ejecutivo",
            SentAt    = DateTime.UtcNow
        };
        await conversations.AddMessageAsync(message, ct);

        // ── Enviar por WhatsApp ───────────────────────────
        bool sent = false;
        try
        {
            var provider = await channelFactory.GetProviderAsync(cmd.TenantId, ct);
            if (provider is not null)
            {
                var result = await provider.SendMessageAsync(
                    new SendMessageRequest(conv.ClientPhone, cmd.Message), ct);

                message.ExternalMessageId = result.ExternalMessageId;
                message.Status = result.Success ? MessageStatus.Sent : MessageStatus.Failed;
                sent = result.Success;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HumanReply] Error enviando WhatsApp: {ex.Message}");
            message.Status = MessageStatus.Failed;
        }

        // ── Pausar IA automáticamente al responder un humano ─
        conv.IsHumanHandled = true;
        conv.HandledByUserId = cmd.SentByUserId;
        conv.Status = ConversationStatus.EscalatedToHuman;
        conv.LastActivityAt = DateTime.UtcNow;
        await conversations.SaveChangesAsync(ct);

        // ── Notificar monitor en tiempo real ──────────────
        try
        {
            await notifier.NotifyMessageAsync(cmd.TenantId.ToString(), new
            {
                Type           = "human_outbound",
                ConversationId = conv.Id,
                Body           = cmd.Message,
                AgentName      = "Ejecutivo",
                Timestamp      = DateTime.UtcNow
            });
        }
        catch { /* SignalR no disponible — no es crítico */ }

        return new HumanReplyResult(message.Id, sent, message.Content, message.SentAt);
    }
}
