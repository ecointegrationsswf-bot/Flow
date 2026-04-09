namespace AgentFlow.Domain.Enums;

/// <summary>
/// Estado de la sesión gestionada por el Cerebro.
/// </summary>
public enum BrainSessionState
{
    /// <summary>Agente IA respondiendo normalmente (inbound o post-transfer).</summary>
    Active_AI,

    /// <summary>Conversación originada por campaña, agente de campaña respondiendo.</summary>
    Active_Campaign,

    /// <summary>Cerebro esperando respuestas del cliente para validar identidad.</summary>
    Pending_Validation,

    /// <summary>Webhook de validación falló o timeout.</summary>
    Validation_Failed,

    /// <summary>Ejecutivo tomó control. Cerebro no interfiere.</summary>
    Escalated_Human,

    /// <summary>Estado terminal. Ejecutivo cerró la conversación.</summary>
    HumanClosed
}
