using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;
using MediatR;

namespace AgentFlow.Application.Modules.Monitor;

/// <summary>
/// El ejecutivo pausa manualmente el agente IA tomando control de la conversación.
/// </summary>
public record PauseAgentCommand(Guid TenantId, Guid ConversationId, string? TakenByUserId)
    : IRequest<PauseAgentResult>;

public record PauseAgentResult(bool Success, string Message);

public class PauseAgentHandler(
    IConversationRepository conversations,
    IConversationNotifier notifier
) : IRequestHandler<PauseAgentCommand, PauseAgentResult>
{
    public async Task<PauseAgentResult> Handle(PauseAgentCommand cmd, CancellationToken ct)
    {
        var conv = await conversations.GetByIdAsync(cmd.ConversationId, ct);
        if (conv is null || conv.TenantId != cmd.TenantId)
            return new PauseAgentResult(false, "Conversación no encontrada");

        conv.IsHumanHandled = true;
        conv.HandledByUserId = cmd.TakenByUserId;
        conv.Status = ConversationStatus.EscalatedToHuman;
        conv.LastActivityAt = DateTime.UtcNow;
        await conversations.SaveChangesAsync(ct);

        try
        {
            await notifier.NotifyMessageAsync(cmd.TenantId.ToString(), new
            {
                Type = "agent_paused",
                ConversationId = conv.Id,
                TakenBy = cmd.TakenByUserId,
                Timestamp = DateTime.UtcNow
            });
        }
        catch { }

        return new PauseAgentResult(true, "Agente IA pausado");
    }
}
