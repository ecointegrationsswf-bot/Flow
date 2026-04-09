using AgentFlow.Domain.Enums;

namespace AgentFlow.Domain.Interfaces;

/// <summary>
/// Orquestador central del Cerebro.
/// Dado un mensaje entrante y el estado de la sesión, decide qué componente debe responder.
/// No genera respuestas, no maneja WhatsApp, no procesa campañas — solo decide y delega.
/// </summary>
public interface IBrainService
{
    Task<BrainDecision> RouteAsync(BrainRequest request, CancellationToken ct = default);
}

public record BrainRequest(
    Guid TenantId,
    string ContactId,
    string Message,
    string Channel,
    Guid? CampaignId = null
);

public record BrainDecision(
    string AgentSlug,
    string Intent,
    BrainSessionState SessionState,
    bool ValidationPending,
    string? MessageToClient = null
);
