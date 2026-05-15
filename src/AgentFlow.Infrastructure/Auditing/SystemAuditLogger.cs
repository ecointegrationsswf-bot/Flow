using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.Auditing;

/// <summary>
/// Implementación de <see cref="ISystemAuditLogger"/>. Persiste en
/// <c>SystemAuditLog</c> usando un DbContext EFÉMERO creado por scope nuevo —
/// CRÍTICO porque el caller a menudo está dentro de un catch donde el DbContext
/// actual puede haber quedado en estado roto tras la excepción (especialmente
/// SaveChanges parciales). Crear un scope dedicado garantiza que el log
/// SIEMPRE se persiste.
///
/// Si el log mismo falla (BD caída, schema desincronizado), se cae con elegancia
/// a Console + ILogger y NUNCA propaga al caller — el flujo de negocio no debe
/// romperse porque la auditoría no se pudo guardar.
/// </summary>
public class SystemAuditLogger(
    IServiceScopeFactory scopeFactory,
    ILogger<SystemAuditLogger> log) : ISystemAuditLogger
{
    private const int MessageMaxLen   = 1000;
    private const int StackMaxLen     = 4000;
    private const int ContextMaxLen   = 4000;

    public Task LogErrorAsync(
        string category, string message, Exception? ex = null,
        Guid? tenantId = null, string? relatedEntityType = null,
        Guid? relatedEntityId = null, string? contextJson = null,
        CancellationToken ct = default)
        => PersistAsync("Error", category, message, ex,
            tenantId, relatedEntityType, relatedEntityId, contextJson, ct);

    public Task LogWarningAsync(
        string category, string message,
        Guid? tenantId = null, string? relatedEntityType = null,
        Guid? relatedEntityId = null, string? contextJson = null,
        CancellationToken ct = default)
        => PersistAsync("Warning", category, message, ex: null,
            tenantId, relatedEntityType, relatedEntityId, contextJson, ct);

    private async Task PersistAsync(
        string severity, string category, string message, Exception? ex,
        Guid? tenantId, string? relatedEntityType, Guid? relatedEntityId,
        string? contextJson, CancellationToken ct)
    {
        try
        {
            // Scope dedicado para evitar que un DbContext en estado roto (típico
            // tras un catch) impida persistir el log.
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AgentFlowDbContext>();

            // Combinamos message + ex.Message para tener todo en un solo campo legible.
            var fullMessage = ex is not null
                ? $"{message} | {ex.GetType().Name}: {ex.Message}"
                : message;

            db.SystemAuditLogs.Add(new SystemAuditLog
            {
                Id                = Guid.NewGuid(),
                OccurredAtUtc     = DateTime.UtcNow,
                TenantId          = tenantId,
                Category          = Truncate(category, 50),
                Severity          = severity,
                Message           = Truncate(fullMessage, MessageMaxLen),
                StackTrace        = ex is null ? null : Truncate(ex.ToString(), StackMaxLen),
                RelatedEntityType = Truncate(relatedEntityType, 50),
                RelatedEntityId   = relatedEntityId,
                Context           = Truncate(contextJson, ContextMaxLen),
            });

            await db.SaveChangesAsync(ct);
        }
        catch (Exception logEx)
        {
            // Última red de seguridad: si no podemos persistir el log, al menos
            // que aparezca en stdout. NUNCA propagar — el caller ya está
            // manejando su propio error.
            log.LogWarning(logEx,
                "[SystemAuditLogger] No se pudo persistir el log de auditoría. " +
                "Categoría={Category} Severity={Severity} Mensaje original: {Msg}",
                category, severity, message);
        }
    }

    private static string? Truncate(string? value, int max)
    {
        if (value is null) return null;
        return value.Length <= max ? value : value[..max];
    }
}
