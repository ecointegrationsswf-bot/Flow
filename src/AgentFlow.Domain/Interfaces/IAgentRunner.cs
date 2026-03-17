using AgentFlow.Domain.Entities;

namespace AgentFlow.Domain.Interfaces;

/// <summary>
/// Ejecuta un agente IA dado el contexto de la conversación.
/// Retorna la respuesta generada y metadata de la clasificación.
/// </summary>
public interface IAgentRunner
{
    Task<AgentResponse> RunAsync(AgentRunRequest request, CancellationToken ct = default);
}

public record AgentRunRequest(
    AgentDefinition Agent,
    Conversation Conversation,
    string IncomingMessage,
    List<Message> RecentHistory,
    Dictionary<string, string>? ClientContext = null   // saldo, póliza, etc.
);

public record AgentResponse(
    string ReplyText,
    string DetectedIntent,     // cobros | reclamos | renovaciones | humano | cierre
    double ConfidenceScore,
    bool ShouldEscalate,
    bool ShouldClose,
    int TokensUsed
);
