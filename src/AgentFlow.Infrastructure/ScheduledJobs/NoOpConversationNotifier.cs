using AgentFlow.Domain.Interfaces;

namespace AgentFlow.Infrastructure.ScheduledJobs;

/// <summary>
/// Implementación vacía de IConversationNotifier para el Worker.
/// El Worker no tiene SignalR Hub, por lo que las notificaciones en tiempo real
/// no aplican. Los executors que notifican a clientes web simplemente no emiten nada.
/// </summary>
public sealed class NoOpConversationNotifier : IConversationNotifier
{
    public Task NotifyMessageAsync(string tenantId, object payload) => Task.CompletedTask;
    public Task NotifyEscalationAsync(string tenantId, Guid conversationId) => Task.CompletedTask;
    public Task NotifyConversationUpdateAsync(Guid conversationId, object payload) => Task.CompletedTask;
}
