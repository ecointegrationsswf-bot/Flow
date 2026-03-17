namespace AgentFlow.Domain.Interfaces;

/// <summary>
/// Almacén de sesiones activas en Redis.
/// Clave: tenantId:phone — Valor: SessionState serializado.
/// Soluciona el problema de relanzamiento de campañas al reconectar.
/// </summary>
public interface ISessionStore
{
    Task<SessionState?> GetAsync(Guid tenantId, string phone, CancellationToken ct = default);
    Task SetAsync(Guid tenantId, string phone, SessionState state, TimeSpan? expiry = null, CancellationToken ct = default);
    Task RemoveAsync(Guid tenantId, string phone, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid tenantId, string phone, CancellationToken ct = default);
}

public record SessionState(
    Guid ConversationId,
    Guid AgentId,
    string AgentType,
    Guid? CampaignId,
    bool IsHumanHandled,
    DateTime LastActivityAt
);
