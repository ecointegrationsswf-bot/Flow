namespace AgentFlow.Domain.Enums;

/// <summary>
/// Política del Cerebro cuando un cliente en campaña se desvía del contexto.
/// </summary>
public enum OutOfContextPolicy
{
    /// <summary>El agente de la campaña gestiona todos los mensajes sin transferir.</summary>
    Contain,

    /// <summary>El Cerebro detecta la nueva intención y ruta al agente más adecuado del AgentRegistry.</summary>
    Transfer
}
