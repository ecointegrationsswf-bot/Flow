using AgentFlow.Domain.Entities;
using AgentFlow.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace AgentFlow.Infrastructure.ScheduledJobs;

/// <summary>
/// Helper único que TODO IScheduledJobExecutor debe usar para registrar el
/// detalle granular de fallos en <c>ScheduledWebhookJobExecutionItems</c>.
///
/// Uso obligatorio (ver regla en el skill `scheduled-jobs`):
/// cualquier executor que procesa una colección (conversaciones, contactos,
/// campañas, tenants, etc.) debe llamar a <see cref="RecordFailureAsync"/>
/// por cada item que falle, con el motivo legible del fallo.
///
/// Esto permite al admin abrir "Historial de ejecuciones" y ver QUÉ TENANT
/// falló y POR QUÉ — sin tener que ir a logs de servidor.
///
/// Convenciones:
/// - <c>contextType</c>: una de las constantes <c>ContextTypes.*</c>.
/// - <c>contextLabel</c>: nombre humano (no GUID). Ej: "Maestro: Cobros Somos".
/// - <c>errorMessage</c>: motivo en español, máximo 500 chars (truncado automático).
///
/// Internamente: acumula items en una lista que se persiste en <see cref="FlushAsync"/>
/// al final de la ejecución (un solo INSERT batch).
/// </summary>
public sealed class JobExecutionAuditor
{
    private readonly IJobExecutionItemRepository _itemRepo;
    private readonly ILogger<JobExecutionAuditor> _log;
    private readonly List<ScheduledWebhookJobExecutionItem> _pending = new();

    public JobExecutionAuditor(
        IJobExecutionItemRepository itemRepo,
        ILogger<JobExecutionAuditor> log)
    {
        _itemRepo = itemRepo;
        _log = log;
    }

    public static class ContextTypes
    {
        public const string Conversation = "Conversation";
        public const string Campaign     = "Campaign";
        public const string Contact      = "Contact";
        public const string Tenant       = "Tenant";
        public const string Template     = "Template";
        public const string User         = "User";
    }

    /// <summary>
    /// Registra un item de fallo. Si <paramref name="executionId"/> es null,
    /// se descarta silenciosamente (executor invocado fuera del Worker, sin
    /// contexto de ejecución).
    /// </summary>
    public void RecordFailure(
        Guid? executionId,
        Guid? tenantId,
        string contextType,
        string contextId,
        string? contextLabel,
        string errorMessage)
    {
        if (executionId is null) return;
        _pending.Add(new ScheduledWebhookJobExecutionItem
        {
            ExecutionId  = executionId.Value,
            TenantId     = tenantId,
            ContextType  = contextType,
            ContextId    = contextId,
            ContextLabel = contextLabel,
            Status       = "Failed",
            ErrorMessage = Truncate(errorMessage, 500),
        });
    }

    /// <summary>
    /// Atajo para registrar un Skipped — útil para casos donde un item NO se
    /// procesó (no es un fallo, pero el admin igual debe ver el motivo).
    /// </summary>
    public void RecordSkipped(
        Guid? executionId,
        Guid? tenantId,
        string contextType,
        string contextId,
        string? contextLabel,
        string reason)
    {
        if (executionId is null) return;
        _pending.Add(new ScheduledWebhookJobExecutionItem
        {
            ExecutionId  = executionId.Value,
            TenantId     = tenantId,
            ContextType  = contextType,
            ContextId    = contextId,
            ContextLabel = contextLabel,
            Status       = "Skipped",
            ErrorMessage = Truncate(reason, 500),
        });
    }

    /// <summary>
    /// Persiste todos los items acumulados en BD. Se debe llamar UNA VEZ al
    /// final de <c>ExecuteAsync</c>, antes de retornar el <c>JobRunResult</c>.
    /// Idempotente: si no hay items pendientes, no toca BD.
    /// </summary>
    public async Task FlushAsync(CancellationToken ct = default)
    {
        if (_pending.Count == 0) return;
        try
        {
            await _itemRepo.AddBatchAsync(_pending, ct);
            _log.LogDebug("JobExecutionAuditor: persistidos {N} items de auditoría.", _pending.Count);
            _pending.Clear();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex,
                "JobExecutionAuditor: no se pudieron persistir {N} items de auditoría — el resumen del job se completó pero el detalle granular no quedó en BD.",
                _pending.Count);
            // No re-lanzamos: la auditoría es best-effort, no debe romper el flujo.
        }
    }

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return s.Length <= max ? s : s[..max];
    }
}
