namespace AgentFlow.Domain.Webhooks;

/// <summary>
/// Parámetros que el agente recolectó del cliente durante la conversación.
/// Se pasan al PayloadBuilder para resolver los campos con SourceType=conversation.
/// En Fase 1-5 siempre va vacío (solo SystemOnly soportado).
/// </summary>
public class CollectedParams
{
    public Dictionary<string, string?> Values { get; init; } = new();

    public static CollectedParams Empty() => new();
}
