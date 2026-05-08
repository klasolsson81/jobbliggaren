using System.Collections.Concurrent;
using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Domain.Common;
using Microsoft.Extensions.Options;

namespace JobbPilot.Infrastructure.Auth.Sessions;

public sealed class InMemorySessionStore(
    IDateTimeProvider dateTimeProvider,
    IOptions<SessionStoreOptions> options) : ISessionStore
{
    private readonly TimeSpan _ttl = options.Value.Ttl;

    private readonly ConcurrentDictionary<string, (Guid UserId, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt)>
        _sessions = new();

    public Task<Session?> GetAsync(SessionId sessionId, CancellationToken ct)
    {
        var key = sessionId.Reveal();
        if (!_sessions.TryGetValue(key, out var entry))
            return Task.FromResult<Session?>(null);

        var now = dateTimeProvider.UtcNow;
        if (entry.ExpiresAt < now)
        {
            _sessions.TryRemove(key, out _);
            return Task.FromResult<Session?>(null);
        }

        var newExpiry = now + _ttl;
        _sessions.TryUpdate(key, (entry.UserId, entry.CreatedAt, newExpiry), entry);

        return Task.FromResult<Session?>(
            new Session(sessionId, entry.UserId, entry.CreatedAt, newExpiry));
    }

    public Task<Session> CreateAsync(Guid userId, CancellationToken ct)
    {
        var sessionId = SessionId.Generate();
        var now = dateTimeProvider.UtcNow;
        var expiresAt = now + _ttl;

        _sessions[sessionId.Reveal()] = (userId, now, expiresAt);

        return Task.FromResult(new Session(sessionId, userId, now, expiresAt));
    }

    public Task<bool> InvalidateAsync(SessionId sessionId, CancellationToken ct)
        => Task.FromResult(_sessions.TryRemove(sessionId.Reveal(), out _));

    public Task<int> InvalidateAllForUserAsync(Guid userId, CancellationToken ct)
    {
        var toRemove = _sessions.Where(kv => kv.Value.UserId == userId).Select(kv => kv.Key).ToList();
        foreach (var key in toRemove)
            _sessions.TryRemove(key, out _);
        return Task.FromResult(toRemove.Count);
    }
}
