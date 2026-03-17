using System.Text.Json;
using AgentFlow.Domain.Interfaces;
using StackExchange.Redis;

namespace AgentFlow.Infrastructure.Session;

public class RedisSessionStore(IConnectionMultiplexer redis) : ISessionStore
{
    private readonly IDatabase _db = redis.GetDatabase();
    private static string Key(Guid tenantId, string phone) => $"session:{tenantId}:{phone}";

    public async Task<SessionState?> GetAsync(Guid tenantId, string phone, CancellationToken ct = default)
    {
        var val = await _db.StringGetAsync(Key(tenantId, phone));
        return val.IsNull ? null : JsonSerializer.Deserialize<SessionState>((string)val!);
    }

    public async Task SetAsync(Guid tenantId, string phone, SessionState state,
        TimeSpan? expiry = null, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(state);
        await _db.StringSetAsync(Key(tenantId, phone), json, expiry ?? TimeSpan.FromHours(72));
    }

    public async Task RemoveAsync(Guid tenantId, string phone, CancellationToken ct = default)
        => await _db.KeyDeleteAsync(Key(tenantId, phone));

    public async Task<bool> ExistsAsync(Guid tenantId, string phone, CancellationToken ct = default)
        => await _db.KeyExistsAsync(Key(tenantId, phone));
}
