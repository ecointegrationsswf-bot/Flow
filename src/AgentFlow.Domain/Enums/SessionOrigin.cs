namespace AgentFlow.Domain.Enums;

/// <summary>
/// Origen del primer contacto de la sesión.
/// </summary>
public enum SessionOrigin
{
    /// <summary>Cliente escribió directamente al número de WhatsApp.</summary>
    Inbound,

    /// <summary>AgentFlow inició la conversación por campaña.</summary>
    Campaign
}
