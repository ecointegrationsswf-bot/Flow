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
    public static JobRunResult Success(int total, string? summary = null)
        => new("Success", total, total, 0, summary);

    public static JobRunResult Partial(int total, int success, int failure, string? summary = null)
        => new("PartialFailure", total, success, failure, summary);

    public static JobRunResult Failed(string error, string? summary = null)
        => new("Failed", 0, 0, 0, summary, error);

    public static JobRunResult Skipped(string? reason = null)
        => new("Skipped", 0, 0, 0, reason);
}
