using AgentFlow.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AgentFlow.API.Hubs;

/// <summary>
/// Hub SignalR para el monitor en vivo del equipo de cobros.
/// Los ejecutivos se unen al grupo de su tenant y reciben eventos en tiempo real.
/// Eventos: MessageReceived, AgentReplied, ConversationEscalated, ConversationClosed.
/// </summary>
[Authorize]
public class ConversationHub : Hub
{
    public async Task JoinTenantGroup(string tenantId)
        => await Groups.AddToGroupAsync(Context.ConnectionId, $"tenant:{tenantId}");

    public async Task LeaveTenantGroup(string tenantId)
        => await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"tenant:{tenantId}");

    public async Task JoinConversation(string conversationId)
        => await Groups.AddToGroupAsync(Context.ConnectionId, $"conv:{conversationId}");
}

/// <summary>
/// Servicio inyectable para emitir eventos desde handlers y commands.
/// </summary>
public class ConversationNotifier(IHubContext<ConversationHub> hub) : IConversationNotifier
{
    public Task NotifyMessageAsync(string tenantId, object payload)
        => hub.Clients.Group($"tenant:{tenantId}").SendAsync("MessageReceived", payload);

    public Task NotifyEscalationAsync(string tenantId, Guid conversationId)
        => hub.Clients.Group($"tenant:{tenantId}").SendAsync("ConversationEscalated", conversationId);

    public Task NotifyConversationUpdateAsync(Guid conversationId, object payload)
        => hub.Clients.Group($"conv:{conversationId}").SendAsync("ConversationUpdated", payload);
}
