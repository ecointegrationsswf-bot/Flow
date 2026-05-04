namespace AgentFlow.Application.Modules.Campaigns.LaunchV2;

/// <summary>
/// Resultado del lanzamiento v2. Refleja el contrato que devolvía n8n al frontend.
/// </summary>
public record LaunchCampaignV2Result(
    bool Success,
    Guid CampaignId,
    string Status,
    int TotalProcessed,
    int QueuedCount,
    int DeferredCount,
    int DuplicateCount,
    int SkippedCount,
    int DailyLimit,
    int WarmupDay,
    DateTime? LaunchedAt,
    string? Error
);
