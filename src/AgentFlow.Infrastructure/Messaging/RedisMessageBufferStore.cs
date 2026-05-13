using System.Text.Json;
using AgentFlow.Domain.Interfaces;
using StackExchange.Redis;

namespace AgentFlow.Infrastructure.Messaging;

/// <summary>
/// Implementación Redis del IMessageBufferStore.
/// Usa LIST para acumular mensajes y strings auxiliares para timestamp + lock.
/// </summary>
public class RedisMessageBufferStore(IConnectionMultiplexer redis) : IMessageBufferStore
{
    // TTL de seguridad — si algo falla los buffers no se acumulan indefinidamente.
    private static readonly TimeSpan SafetyTtl = TimeSpan.FromMinutes(2);

    private static string ListKey(Guid tenantId, string phone) => $"buf:{tenantId:N}:{phone}";
    private static string TsKey(Guid tenantId, string phone)   => $"bufts:{tenantId:N}:{phone}";
    private static string LockKey(Guid tenantId, string phone) => $"buflock:{tenantId:N}:{phone}";

    public async Task AppendAsync(Guid tenantId, string phone, BufferedMessage msg, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var payload = JsonSerializer.Serialize(msg);
        var now = DateTime.UtcNow.Ticks;

        var batch = db.CreateBatch();
        var t1 = batch.ListRightPushAsync(ListKey(tenantId, phone), payload);
        var t2 = batch.KeyExpireAsync(ListKey(tenantId, phone), SafetyTtl);
        var t3 = batch.StringSetAsync(TsKey(tenantId, phone), now, SafetyTtl);
        batch.Execute();
        await Task.WhenAll(t1, t2, t3);
    }

    public async Task<long?> GetLastMessageTicksAsync(Guid tenantId, string phone, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var val = await db.StringGetAsync(TsKey(tenantId, phone));
        if (!val.HasValue) return null;
        return long.TryParse(val.ToString(), out var ticks) ? ticks : null;
    }

    public async Task<List<BufferedMessage>> DrainAsync(Guid tenantId, string phone, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var key = ListKey(tenantId, phone);
        // Leer todo y borrar atómicamente — usamos una transacción optimista.
        var tx = db.CreateTransaction();
        var rangeTask = tx.ListRangeAsync(key);
        _ = tx.KeyDeleteAsync(key);
        _ = tx.KeyDeleteAsync(TsKey(tenantId, phone));
        var ok = await tx.ExecuteAsync();
        if (!ok) return [];

        var raw = await rangeTask;
        var result = new List<BufferedMessage>(raw.Length);
        foreach (var r in raw)
        {
            if (!r.HasValue) continue;
            try
            {
                var msg = JsonSerializer.Deserialize<BufferedMessage>(r.ToString());
                if (msg is not null) result.Add(msg);
            }
            catch { /* descartamos entrada corrupta */ }
        }
        return result;
    }

    public async Task<bool> TryAcquireFlushLockAsync(Guid tenantId, string phone, TimeSpan ttl, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        return await db.StringSetAsync(LockKey(tenantId, phone), "1", ttl, When.NotExists);
    }

    public async Task ReleaseFlushLockAsync(Guid tenantId, string phone, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        await db.KeyDeleteAsync(LockKey(tenantId, phone));
    }
}
