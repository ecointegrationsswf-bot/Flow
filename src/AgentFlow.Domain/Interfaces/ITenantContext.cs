namespace AgentFlow.Domain.Interfaces;

/// <summary>
/// Provee el tenant activo en el contexto del request HTTP.
/// Resuelto por TenantMiddleware a partir del webhook token o JWT claim.
/// </summary>
public interface ITenantContext
{
    Guid TenantId { get; }
    string TenantSlug { get; }
}
