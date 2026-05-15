namespace AgentFlow.Domain.Interfaces;

/// <summary>
/// Servicio de logging hacia la tabla <c>SystemAuditLog</c>. Los métodos son
/// tolerantes a fallos — si la BD está caída, NO propagan excepciones (el caller
/// ya está en un catch). Sirven para garantizar que CUALQUIER error operacional
/// quede registrado en BD para auditoría sin requerir leer logs del servidor.
/// </summary>
public interface ISystemAuditLogger
{
    /// <summary>
    /// Registra un error/excepción. Si <paramref name="ex"/> tiene valor, se
    /// captura el tipo + mensaje + stack trace. Garantiza que NUNCA propaga
    /// excepciones — si persistir falla, log a Console y sigue.
    /// </summary>
    Task LogErrorAsync(
        string category,
        string message,
        Exception? ex = null,
        Guid? tenantId = null,
        string? relatedEntityType = null,
        Guid? relatedEntityId = null,
        string? contextJson = null,
        CancellationToken ct = default);

    /// <summary>
    /// Variante para eventos no-erróneos pero auditables (advertencias,
    /// reintentos, eventos de seguridad). Severity = "Warning" o "Info".
    /// </summary>
    Task LogWarningAsync(
        string category,
        string message,
        Guid? tenantId = null,
        string? relatedEntityType = null,
        Guid? relatedEntityId = null,
        string? contextJson = null,
        CancellationToken ct = default);
}
