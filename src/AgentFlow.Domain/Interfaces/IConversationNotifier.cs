namespace AgentFlow.Domain.Interfaces;

/// <summary>
/// Notificador de eventos de conversación en tiempo real.
/// La implementación usa SignalR para enviar eventos al frontend (monitor).
/// Definido como interfaz en Domain para que Application pueda usarlo sin depender de SignalR.
/// </summary>
public interface IConversationNotifier
{
    Task NotifyMessageAsync(string tenantId, object payload);
    Task NotifyEscalationAsync(string tenantId, Guid conversationId);
    Task NotifyConversationUpdateAsync(Guid conversationId, object payload);
}
