using AgentFlow.Domain.Entities;

namespace AgentFlow.Domain.Flows;

/// <summary>
/// Motor de flujos — Fase 3. Punto de entrada del motor de workflows. Lo invoca el handler del
/// inbound una vez por turno. Si el maestro de la conversación tiene un flujo activo, devuelve una
/// <see cref="FlowSession"/> lista para compilar el WorkflowBlock y gestionar el pendingRequest.
/// Si no hay flujo (o no está activo / vacío / no carga), devuelve null y el inbound se comporta
/// EXACTAMENTE como antes (aditivo, sin regresión).
/// </summary>
public interface IWorkflowEngine
{
    Task<FlowSession?> StartTurnAsync(Guid tenantId, Guid? activeFlowId, Conversation conversation, CancellationToken ct = default);
}
