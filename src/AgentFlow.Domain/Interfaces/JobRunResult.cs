namespace AgentFlow.Domain.Interfaces;

/// <summary>
/// Resultado de la ejecución de un ScheduledWebhookJob por parte del Worker.
/// Construido por los executors específicos (FollowUp, AutoClose, Labeling, etc.)
/// y consumido por el Worker para persistir la ejecución y actualizar el job.
/// </summary>
public sealed record JobRunResult(
    string Status,           // Success | PartialFailure | Failed | Skipped
    int TotalRecords,
    int SuccessCount,
    int FailureCount,
    string? Summary = null,
    string? ErrorDetail = null)
{
    /// <summary>
    /// Si el executor decidió "posponer" el job (ej: fuera de horario laboral),
    /// expone aquí el próximo instante UTC en el que debe correr. El Worker lo
    /// usará para sobreescribir <c>NextRunAt</c> en lugar de tratar el job como
    /// completado. NULL = usar el cómputo normal (Cron/null para DelayFromEvent).
    /// </summary>
    public DateTime? RescheduleAt { get; init; }

    public static JobRunResult Success(int total, string? summary = null)
        => new("Success", total, total, 0, summary);

    public static JobRunResult Partial(int total, int success, int failure, string? summary = null)
        => new("PartialFailure", total, success, failure, summary);

    public static JobRunResult Failed(string error, string? summary = null)
        => new("Failed", 0, 0, 0, summary, error);

    public static JobRunResult Skipped(string? reason = null)
        => new("Skipped", 0, 0, 0, reason);

    /// <summary>
    /// Indica al Worker que el job NO se ejecutó pero debe reintentarse en
    /// <paramref name="nextUtc"/>. Útil para diferir follow-ups que cayeron
    /// fuera del horario laboral del tenant.
    /// </summary>
    public static JobRunResult Deferred(DateTime nextUtc, string reason)
        => new("Skipped", 0, 0, 0, reason) { RescheduleAt = nextUtc };
}
