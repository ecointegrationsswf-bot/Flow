namespace AgentFlow.Domain.Enums;

/// <summary>
/// De dónde vienen los parámetros de entrada de una acción.
/// </summary>
public enum ParamSource
{
    /// <summary>
    /// Todos los datos disponibles en runtime: sesión Redis, contacto, campaña, tenant.
    /// El agente puede disparar la acción de inmediato sin preguntar nada al cliente.
    /// </summary>
    SystemOnly,

    /// <summary>
    /// El cliente provee todos los datos durante la conversación.
    /// El agente DEBE recolectar todos los RequiredParams antes de declarar la acción.
    /// </summary>
    ConversationOnly,

    /// <summary>
    /// Algunos datos del sistema, otros del chat.
    /// El agente consulta InputSchema para identificar qué campos pedir al cliente.
    /// </summary>
    Mixed
}
