namespace AgentFlow.Domain.Webhooks;

/// <summary>
/// Contexto necesario para que OutputInterpreter ejecute cada outputAction.
/// Contiene referencias de runtime (tenant, contacto, sesión) sin lógica.
/// </summary>
public class OutputContext
{
    public Guid TenantId { get; init; }
    public string ContactPhone { get; init; } = string.Empty;
    public Guid? ConversationId { get; init; }

    /// <summary>Slug del agente actual — para audit log.</summary>
    public string? AgentSlug { get; init; }

    /// <summary>Nombre de la acción ejecutada — para audit log.</summary>
    public string ActionName { get; init; } = string.Empty;
}
