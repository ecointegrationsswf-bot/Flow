using AgentFlow.Domain.Entities;

namespace AgentFlow.Domain.Interfaces;

/// <summary>
/// Cuando el agente cierra exitosamente una conversación ([INTENT:cierre])
/// y el maestro de campaña tiene vinculada la acción SEND_EMAIL_RESUME,
/// envía un email al ejecutivo con el resumen completo de la gestión.
/// </summary>
public interface ISendEmailResumeService
{
    Task ExecuteIfApplicableAsync(Conversation conversation, CancellationToken ct = default);
}
