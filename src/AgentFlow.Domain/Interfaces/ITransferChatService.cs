using AgentFlow.Domain.Entities;

namespace AgentFlow.Domain.Interfaces;

/// <summary>
/// Servicio que notifica al ejecutivo de campaña cuando un cliente solicita
/// atención humana ([INTENT:humano]) y la campaña tiene vinculada la acción TRANSFER_CHAT.
/// </summary>
public interface ITransferChatService
{
    /// <summary>
    /// Procesa el escalamiento a humano. Devuelve <c>true</c> si el maestro de la
    /// campaña tiene vinculada la acción <c>TRANSFER_CHAT</c> — en ese caso el caller
    /// debe pausar al agente (<c>conversation.IsHumanHandled = true</c>) para que
    /// el cliente no siga recibiendo respuestas del agente IA hasta que un ejecutivo
    /// responda desde el monitor. Si devuelve <c>false</c>, el template no tiene la
    /// acción y el agente sigue activo (comportamiento previo). El cooldown
    /// (LastTransferChatSentAt) solo afecta a la notificación; la pausa se mantiene
    /// mientras el template tenga la acción vinculada.
    ///
    /// <paramref name="forceRenotify"/> (escalamiento robusto Fase E): cuando es true, IGNORA el
    /// cooldown y vuelve a notificar al ejecutivo (lo usa el watchdog de conversaciones escaladas
    /// sin atender para enviar recordatorios). Default false = comportamiento histórico.
    /// </summary>
    Task<bool> ExecuteIfApplicableAsync(Conversation conversation, CancellationToken ct = default, bool forceRenotify = false);
}
