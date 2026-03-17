using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Enums;
using AgentFlow.Domain.Interfaces;
using MediatR;

namespace AgentFlow.Application.Modules.Webhooks;

/// <summary>
/// Comando principal: procesa un mensaje entrante de cualquier canal.
/// Orquesta: Dispatcher → Agente → Persistencia → SignalR notification.
/// Llamado por n8n vía POST /api/webhooks/message o directamente por el Controller.
/// </summary>
public record ProcessIncomingMessageCommand(
    Guid TenantId,
    string FromPhone,
    string Message,
    ChannelType Channel,
    string? ClientName,
    string? ExternalMessageId
) : IRequest<ProcessIncomingMessageResult>;

public record ProcessIncomingMessageResult(
    Guid ConversationId,
    string ReplyText,
    bool WasEscalated,
    bool WasClosed,
    string AgentType
);

public class ProcessIncomingMessageHandler(
    IContextDispatcher dispatcher,
    IAgentRunner agentRunner,
    IConversationRepository conversations,
    ISessionStore sessions,
    IMediator mediator
) : IRequestHandler<ProcessIncomingMessageCommand, ProcessIncomingMessageResult>
{
    public async Task<ProcessIncomingMessageResult> Handle(
        ProcessIncomingMessageCommand cmd, CancellationToken ct)
    {
        // 1. Dispatcher: ¿qué agente responde?
        var dispatch = await dispatcher.DispatchAsync(new(
            cmd.TenantId, cmd.FromPhone, cmd.Message, cmd.Channel.ToString()), ct);

        // 2. Obtener o crear conversación
        Conversation conversation;
        if (dispatch.IsExistingSession && dispatch.ExistingConversationId.HasValue)
        {
            conversation = (await conversations.GetByIdAsync(dispatch.ExistingConversationId.Value, ct))!;
        }
        else
        {
            conversation = new Conversation
            {
                Id = Guid.NewGuid(),
                TenantId = cmd.TenantId,
                ClientPhone = cmd.FromPhone,
                ClientName = cmd.ClientName,
                Channel = cmd.Channel,
                ActiveAgentId = dispatch.SelectedAgentId,
                CampaignId = null,
                Status = ConversationStatus.Active,
                StartedAt = DateTime.UtcNow,
                LastActivityAt = DateTime.UtcNow
            };
            conversation = await conversations.CreateAsync(conversation, ct);
        }

        // 3. Registrar mensaje entrante
        var inbound = new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = conversation.Id,
            Direction = MessageDirection.Inbound,
            Status = MessageStatus.Delivered,
            Content = cmd.Message,
            ExternalMessageId = cmd.ExternalMessageId,
            IsFromAgent = false,
            SentAt = DateTime.UtcNow
        };
        conversation.Messages.Add(inbound);

        // 4. Ejecutar agente (si no está manejado por humano)
        AgentResponse? agentResponse = null;
        if (!conversation.IsHumanHandled && dispatch.SelectedAgentId.HasValue)
        {
            // Aquí se resuelve el agente desde el contenedor — simplificado para el scaffold
            agentResponse = new AgentResponse(
                "Procesando...", dispatch.Intent, 0.9, false, false, 0);
        }

        // 5. Actualizar sesión en Redis
        await sessions.SetAsync(cmd.TenantId, cmd.FromPhone, new SessionState(
            conversation.Id,
            dispatch.SelectedAgentId ?? Guid.Empty,
            dispatch.Intent,
            null,
            conversation.IsHumanHandled,
            DateTime.UtcNow
        ), TimeSpan.FromHours(72), ct);

        // 6. Persistir
        conversation.LastActivityAt = DateTime.UtcNow;
        await conversations.UpdateAsync(conversation, ct);

        return new ProcessIncomingMessageResult(
            conversation.Id,
            agentResponse?.ReplyText ?? string.Empty,
            agentResponse?.ShouldEscalate ?? false,
            agentResponse?.ShouldClose ?? false,
            dispatch.Intent
        );
    }
}
