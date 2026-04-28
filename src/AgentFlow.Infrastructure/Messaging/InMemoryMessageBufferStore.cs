using System.Collections.Concurrent;
using AgentFlow.Domain.Interfaces;

namespace AgentFlow.Infrastructure.Messaging;

/// <summary>
/// Fallback en memoria — se usa sólo en dev/tests cuando Redis no está disponible.
/// En producción se usa RedisMessageBufferStore (single-node).
/// </summary>
public class InMemoryMessageBufferStore : IMessageBufferStore
{
    private static readonly ConcurrentDictionary<string, ConversationBuffer> _store = new();
    private static readonly ConcurrentDictionary<string, (DateTime Expiry, bool Held)> _locks = new();

    private static string Key(Guid tenantId, string phone) => $"{tenantId:N}:{phone}";

    public Task AppendAsync(Guid tenantId, string phone, BufferedMessage msg, CancellationToken ct = default)
    {
        var entry = _store.GetOrAdd(Key(tenantId, phone), _ => new ConversationBuffer());
        lock (entry.Lock)
        {
            entry.Messages.Add(msg);
            entry.LastTicks = msg.TimestampTicks;
        }
        return Task.CompletedTask;
    }

    public Task<long?> GetLastMessageTicksAsync(Guid tenantId, string phone, CancellationToken ct = default)
    {
        if (_store.TryGetValue(Key(tenantId, phone), out var entry))
        {
            lock (entry.Lock)
            {
                return Task.FromResult<long?>(entry.LastTicks);
            }
        }
        return Task.FromResult<long?>(null);
    }

    public Task<List<BufferedMessage>> DrainAsync(Guid tenantId, string phone, CancellationToken ct = default)
    {
        var key = Key(tenantId, phone);
        if (!_store.TryGetValue(key, out var entry)) return Task.FromResult(new List<BufferedMessage>());
        List<BufferedMessage> drained;
        lock (entry.Lock)
        {
            drained = [.. entry.Messages];
            entry.Messages.Clear();
            entry.LastTicks = 0;
        }
        _store.TryRemove(key, out _);
        return Task.FromResult(drained);
    }

    public Task<bool> TryAcquireFlushLockAsync(Guid tenantId, string phone, TimeSpan ttl, CancellationToken ct = default)
    {
        var key = Key(tenantId, phone);
        var now = DateTime.UtcNow;
        var iWonTheRace = false;
        _locks.AddOrUpdate(
            key,
            _ =>
            {
                // No había lock → lo tomo yo.
                iWonTheRace = true;
                return (now.Add(ttl), true);
            },
            (_, existing) =>
            {
                if (existing.Held && existing.Expiry > now)
                {
                    // Lock vigente de otro job — NO lo tomo, dejo el existente.
                    iWonTheRace = false;
                    return existing;
                }
                // Lock expirado o liberado → lo tomo yo.
                iWonTheRace = true;
                return (now.Add(ttl), true);
            });
        return Task.FromResult(iWonTheRace);
    }

    public Task ReleaseFlushLockAsync(Guid tenantId, string phone, CancellationToken ct = default)
    {
        _locks.TryRemove(Key(tenantId, phone), out _);
        return Task.CompletedTask;
    }

    private class ConversationBuffer
    {
        public readonly object Lock = new();
        public readonly List<BufferedMessage> Messages = [];
        public long LastTicks;
    }
}
