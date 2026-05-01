namespace AgentFlow.Domain.Interfaces;

/// <summary>
/// Servicio que valida una campaña pendiente y dispara el webhook de n8n para iniciar
/// el envío. Reutilizable desde el controller (lanzamiento manual del ejecutivo) y
/// desde procesos automáticos (DelinquencyProcessor con autoCrearCampanas=true).
/// </summary>
public interface ICampaignLauncher
{
    Task<CampaignLaunchResult> LaunchAsync(
        Guid campaignId,
        string launchedByUserId,
        string? launchedByUserPhone,
        CancellationToken ct = default);
}

public record CampaignLaunchResult(
    bool Success,
    Guid CampaignId,
    string Status,
    int PendingContacts,
    DateTime? LaunchedAt,
    string? Error);
