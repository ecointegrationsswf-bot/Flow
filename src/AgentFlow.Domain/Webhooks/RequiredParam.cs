namespace AgentFlow.Domain.Webhooks;

/// <summary>
/// Definición de un parámetro requerido por una acción.
/// Se persiste en ActionDefinition.RequiredParams (JSON array).
/// El agente IA usa esta definición para recolectar el dato del cliente
/// cuando ParamSource = Mixed o ConversationOnly.
/// </summary>
public class RequiredParam
{
    /// <summary>Nombre del parámetro. Ej: "amount", "documentNumber", "birthDate".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>system | conversation — de dónde viene el valor.</summary>
    public string Source { get; set; } = "conversation";

    public bool Required { get; set; } = true;

    /// <summary>Descripción legible del parámetro.</summary>
    public string? Description { get; set; }

    /// <summary>Prompt que el agente usa para pedir el dato al cliente.</summary>
    public string? AgentPrompt { get; set; }
}
