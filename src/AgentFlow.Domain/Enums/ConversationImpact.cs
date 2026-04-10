namespace AgentFlow.Domain.Enums;

/// <summary>
/// Qué pasa en la conversación después de ejecutar una acción.
/// </summary>
public enum ConversationImpact
{
    /// <summary>
    /// El agente necesita la respuesta del webhook para su próximo mensaje.
    /// await completo. DataForAgent se pasa al AgentRunner.
    /// </summary>
    BlocksResponse,

    /// <summary>
    /// El agente ya respondió. El webhook opera en background.
    /// Fire-and-forget, resultado solo en audit log.
    /// </summary>
    Transparent,

    /// <summary>
    /// El resultado enriquece la sesión para turnos futuros.
    /// Guardado en BrainSession.ActionContext. Disponible en los próximos turnos.
    /// </summary>
    UpdatesContext
}
