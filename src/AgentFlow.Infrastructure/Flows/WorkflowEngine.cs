using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Flows;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.Flows;

/// <summary>
/// Motor de flujos — Fase 3. Implementación de <see cref="IWorkflowEngine"/>: resuelve el TenantFlow
/// activo del maestro y construye la <see cref="FlowSession"/> del turno. Tolerante a fallos: ante
/// cualquier problema (flujo inexistente, inactivo, JSON inválido) devuelve null → el inbound sigue
/// como si no hubiera flujo (sin regresión).
/// </summary>
public sealed class WorkflowEngine(AgentFlowDbContext db, ILogger<WorkflowEngine> logger) : IWorkflowEngine
{
    public async Task<FlowSession?> StartTurnAsync(Guid tenantId, Guid? activeFlowId, Conversation conversation, CancellationToken ct = default)
    {
        if (activeFlowId is not { } flowId || flowId == Guid.Empty)
            return null;

        TenantFlow? flow;
        try
        {
            flow = await db.TenantFlows.AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == flowId && f.TenantId == tenantId, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Flow] No se pudo cargar el flujo {FlowId} (tenant {TenantId})", flowId, tenantId);
            return null;
        }

        if (flow is null || !flow.IsActive)
            return null;

        var graph = FlowGraph.Parse(flow.FlowJson);
        if (graph.Nodes.Count == 0)
            return null; // lienzo vacío → nada que guiar

        return new FlowSession(flow.Id, flow.Name, flow.Description, graph, conversation);
    }
}
