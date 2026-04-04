using AgentFlow.Domain.Entities;

namespace AgentFlow.Domain.Interfaces;

/// <summary>
/// Decide qué agente debe manejar un mensaje entrante.
/// Lógica: sesión activa → campaña activa → clasificación LLM.
/// </summary>
public interface IContextDispatcher
{
    Task<DispatchResult> DispatchAsync(DispatchRequest request, CancellationToken ct = default);
}

public record DispatchRequest(
    Guid TenantId,
    string FromPhone,
    string IncomingMessage,
    string Channel
);

public record DispatchResult(
    Guid? ExistingConversationId,
    Guid? SelectedAgentId,
    string Intent,             // cobros | reclamos | renovaciones | humano | new
    bool IsExistingSession,
    bool IsCampaignContact,
    Guid? CampaignId = null    // ID de la campaña si el contacto pertenece a una
);
