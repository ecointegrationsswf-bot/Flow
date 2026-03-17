namespace AgentFlow.Infrastructure.Channels.UltraMsg;

public interface IUltraMsgInstanceService
{
    Task<UltraMsgInstanceStatus> GetStatusAsync(string instanceId, string token, CancellationToken ct = default);
    Task<byte[]> GetQrCodeAsync(string instanceId, string token, CancellationToken ct = default);
    Task<bool> RestartAsync(string instanceId, string token, CancellationToken ct = default);
    Task<bool> LogoutAsync(string instanceId, string token, CancellationToken ct = default);
}

public record UltraMsgInstanceStatus(string Status, string? Phone);
