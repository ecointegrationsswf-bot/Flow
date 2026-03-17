using System.Collections.Concurrent;
using AgentFlow.Domain.Interfaces;

namespace AgentFlow.Infrastructure.Session;

/// <summary>
/// SessionStore en memoria para desarrollo cuando Redis no está disponible.
/// No persiste entre reinicios del API.
/// </summary>
public class InMemorySessionStore : ISessionStore
{
    private readonly ConcurrentDictionary<string, SessionState> _store = new();

    private static string Key(Guid tenantId, string phone) => $"session:{tenantId}:{phone}";

    public Task<SessionState?> GetAsync(Guid tenantId, string phone, CancellationToken ct = default)
    {
        _store.TryGetValue(Key(tenantId, phone), out var state);
        return Task.FromResult(state);
    }

    public Task SetAsync(Guid tenantId, string phone, SessionState state,
        TimeSpan? expiry = null, CancellationToken ct = default)
    {
        _store[Key(tenantId, phone)] = state;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(Guid tenantId, string phone, CancellationToken ct = default)
    {
        _store.TryRemove(Key(tenantId, phone), out _);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(Guid tenantId, string phone, CancellationToken ct = default)
    {
        return Task.FromResult(_store.ContainsKey(Key(tenantId, phone)));
    }
}
