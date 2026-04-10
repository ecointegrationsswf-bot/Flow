using AgentFlow.Domain.Enums;

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
    // ── Campos originales (no modificar orden) ──
    Guid ConversationId,
    Guid AgentId,
    string AgentType,
    Guid? CampaignId,
    bool IsHumanHandled,
    DateTime LastActivityAt,

    // ── Campos del Cerebro (opcionales — compatibles con sesiones existentes en Redis) ──
    BrainSessionState BrainState = BrainSessionState.Active_AI,
    SessionOrigin Origin = SessionOrigin.Inbound,
    Guid? ActiveCampaignId = null,
    string? ActiveAgentSlug = null,
    List<string>? IntentHistory = null,
    ValidationContext? ValidationState = null,
    DateTime? EscalatedAt = null,

    // ── Campos del Webhook Contract System (Fase 3+) ──
    // ActionContext: diccionario que crece con los valores devueltos por outputAction=inject_context.
    // Disponible para el agente en turnos futuros. Default null para compat con sesiones existentes.
    Dictionary<string, string>? ActionContext = null
);
