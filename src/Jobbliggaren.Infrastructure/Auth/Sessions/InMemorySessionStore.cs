using System.Collections.Concurrent;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Infrastructure.Auth.Sessions;

public sealed class InMemorySessionStore(
    IDateTimeProvider dateTimeProvider,
    IOptions<SessionStoreOptions> options) : ISessionStore
{
    private readonly SessionStoreOptions _options = options.Value;

    private readonly ConcurrentDictionary<string, (Guid UserId, DateTimeOffset CreatedAt, DateTimeOffset RotatedAt, DateTimeOffset ExpiresAt, SessionLifetime Lifetime)>
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

        var profile = _options.ProfileFor(entry.Lifetime);

        // Absolute lifetime cap (#481 Low) — mirrors RedisSessionStore so fake-store
        // unit tests and Testcontainers integration tests agree. Inclusive (>=) for
        // parity: at exactly the ceiling the session is spent.
        if (now - entry.CreatedAt >= profile.AbsoluteTtl)
        {
            _sessions.TryRemove(key, out _);
            return Task.FromResult<Session?>(null);
        }

        // Slide up to SlidingTtl, clamped so it never passes the absolute cap.
        var capRemaining = entry.CreatedAt + profile.AbsoluteTtl - now;
        var slidingTtl = capRemaining < profile.SlidingTtl ? capRemaining : profile.SlidingTtl;
        var newExpiry = now + slidingTtl;
        _sessions.TryUpdate(key, (entry.UserId, entry.CreatedAt, entry.RotatedAt, newExpiry, entry.Lifetime), entry);

        return Task.FromResult<Session?>(
            new Session(sessionId, entry.UserId, entry.CreatedAt, newExpiry));
    }

    public Task<Session> CreateAsync(Guid userId, SessionLifetime lifetime, CancellationToken ct)
    {
        var sessionId = SessionId.Generate();
        var now = dateTimeProvider.UtcNow;
        var expiresAt = now + _options.ProfileFor(lifetime).SlidingTtl;

        // RotatedAt starts at CreatedAt (== now).
        _sessions[sessionId.Reveal()] = (userId, now, now, expiresAt, lifetime);

        return Task.FromResult(new Session(sessionId, userId, now, expiresAt));
    }

    public Task<SessionRotation?> RotateAsync(SessionId current, CancellationToken ct)
    {
        var key = current.Reveal();
        if (!_sessions.TryGetValue(key, out var entry))
            return Task.FromResult<SessionRotation?>(null);

        var profile = _options.ProfileFor(entry.Lifetime);
        if (profile.RotationInterval <= TimeSpan.Zero)
            return Task.FromResult<SessionRotation?>(null);

        var now = dateTimeProvider.UtcNow;
        if (now - entry.CreatedAt >= profile.AbsoluteTtl)
            return Task.FromResult<SessionRotation?>(null);

        var rotatedAt = entry.RotatedAt == default ? entry.CreatedAt : entry.RotatedAt;
        if (now - rotatedAt < profile.RotationInterval)
            return Task.FromResult<SessionRotation?>(null);

        // Single-winner: the caller that removes the old key wins (mirrors the Redis
        // SET NX election). A loser gets null and keeps using `current`. No grace window
        // — that is a Redis transport detail for the concurrent-render race; the observable
        // contract (single-winner, CreatedAt preserved, new id valid, old id retired) is
        // identical.
        if (!_sessions.TryRemove(key, out var claimed))
            return Task.FromResult<SessionRotation?>(null);

        var newId = SessionId.Generate();
        var capRemaining = claimed.CreatedAt + profile.AbsoluteTtl - now;
        var slidingTtl = capRemaining < profile.SlidingTtl ? capRemaining : profile.SlidingTtl;
        var expiresAt = now + slidingTtl;

        // Preserve CreatedAt + Lifetime verbatim (cap anchor never resets); RotatedAt = now.
        _sessions[newId.Reveal()] = (claimed.UserId, claimed.CreatedAt, now, expiresAt, claimed.Lifetime);

        return Task.FromResult<SessionRotation?>(new SessionRotation(newId, expiresAt));
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
