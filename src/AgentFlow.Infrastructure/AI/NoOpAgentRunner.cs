using AgentFlow.Domain.Interfaces;

namespace AgentFlow.Infrastructure.AI;

/// <summary>
/// AgentRunner stub para desarrollo cuando no hay API key de Anthropic configurado.
/// Retorna respuesta genérica indicando que el LLM no está configurado.
/// </summary>
public class NoOpAgentRunner : IAgentRunner
{
    public Task<AgentResponse> RunAsync(AgentRunRequest request, CancellationToken ct = default)
    {
        return Task.FromResult(new AgentResponse(
            ReplyText: "[LLM no configurado] Recibido: " + request.IncomingMessage,
            DetectedIntent: "general",
            ConfidenceScore: 0.0,
            ShouldEscalate: false,
            ShouldClose: false,
            TokensUsed: 0
        ));
    }
}
