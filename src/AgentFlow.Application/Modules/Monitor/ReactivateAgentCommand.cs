using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;
using MediatR;

namespace AgentFlow.Application.Modules.Monitor;

/// <summary>
/// El ejecutivo devuelve el control al agente IA en una conversación.
/// </summary>
public record ReactivateAgentCommand(Guid TenantId, Guid ConversationId)
    : IRequest<ReactivateAgentResult>;

public record ReactivateAgentResult(bool Success, string Message);

public class ReactivateAgentHandler(
    IConversationRepository conversations,
    IConversationNotifier notifier
) : IRequestHandler<ReactivateAgentCommand, ReactivateAgentResult>
{
    public async Task<ReactivateAgentResult> Handle(ReactivateAgentCommand cmd, CancellationToken ct)
    {
        var conv = await conversations.GetByIdAsync(cmd.ConversationId, ct);
        if (conv is null || conv.TenantId != cmd.TenantId)
            return new ReactivateAgentResult(false, "Conversación no encontrada");

        conv.IsHumanHandled = false;
        conv.HandledByUserId = null;
        conv.Status = ConversationStatus.Active;
        conv.LastActivityAt = DateTime.UtcNow;
        await conversations.SaveChangesAsync(ct);

        try
        {
            await notifier.NotifyMessageAsync(cmd.TenantId.ToString(), new
            {
                Type = "agent_reactivated",
                ConversationId = conv.Id,
                Timestamp = DateTime.UtcNow
            });
        }
        catch { }

        return new ReactivateAgentResult(true, "Agente IA reactivado");
    }
}
