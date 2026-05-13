using AgentFlow.Domain.Entities;

namespace AgentFlow.Domain.Interfaces;

/// <summary>
/// Servicio que notifica al ejecutivo de campaña cuando un cliente solicita
/// atención humana ([INTENT:humano]) y la campaña tiene vinculada la acción TRANSFER_CHAT.
/// </summary>
public interface ITransferChatService
{
    Task ExecuteIfApplicableAsync(Conversation conversation, CancellationToken ct = default);
}
