namespace AgentFlow.Domain.Enums;

/// <summary>
/// Cuándo se ejecuta una acción con webhook.
/// </summary>
public enum ExecutionMode
{
    /// <summary>
    /// Durante la conversación activa. ActionExecutorService espera la respuesta
    /// antes de continuar. El agente usa el resultado para responder al cliente.
    /// </summary>
    Inline,

    /// <summary>
    /// Inmediatamente después de que el agente ya respondió. Se ejecuta en background,
    /// no bloquea, fallos silenciosos al cliente (solo en audit log).
    /// </summary>
    FireAndForget,

    /// <summary>
    /// Por horario o evento del sistema. Sin gatillo de mensaje.
    /// Ejecutado por un Hangfire job, fuera del scope del ActionExecutorService.
    /// </summary>
    Scheduled
}
