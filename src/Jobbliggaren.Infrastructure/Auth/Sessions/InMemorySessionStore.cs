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

    // SupersededAt (2b-3) mirrors RedisSessionStore.SessionPayload: null = live, set = the key
    // has been rotated away and is living out its fixed grace window (COND-A). ExpiresAt then
    // holds the grace ceiling (now + RotationGraceWindow) and is never slid.
    private readonly ConcurrentDictionary<string, (Guid UserId, DateTimeOffset CreatedAt, DateTimeOffset RotatedAt, DateTimeOffset ExpiresAt, SessionLifetime Lifetime, DateTimeOffset? SupersededAt)>
        _sessions = new();

    // COND-B revocation tombstone: userId -> revoked-until. Mirrors the Redis SET EX tombstone
    // — InvalidateAllForUserAsync plants it, RotateAsync fails closed while it is live.
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _revoked = new();

    // PR2c-0 Layer 2 account-deletion tombstone: userId -> deleted-until. Mirrors the Redis
    // SET EX :deleted tombstone — MarkUserDeletedAsync plants it, GetAsync fails closed while live.
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _deleted = new();

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

        // PR2c-0 Layer 2 — fail-closed account-deletion gate (mirrors RedisSessionStore.GetAsync):
        // a per-user :deleted tombstone rejects + evicts EVERY surviving session for a deleted
        // user, closing the read-path erasure gap. Before the cap + COND-A grace checks.
        if (IsDeleted(entry.UserId, now))
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

        // COND-A grace: a superseded key still authenticates so concurrent in-flight requests
        // bearing the old id don't 401 — but it is NOT slid (its ExpiresAt is the fixed grace
        // ceiling set at rotation). Mirrors RedisSessionStore.GetAsync's short-circuit.
        if (entry.SupersededAt is not null)
            return Task.FromResult<Session?>(
                new Session(sessionId, entry.UserId, entry.CreatedAt, entry.ExpiresAt, entry.Lifetime));

        // NOTE (#746): the RedisSessionStore sliding-write throttle (SlidAt / SlideThreshold) is
        // deliberately NOT mirrored here. The throttle exists solely to skip the per-read Redis
        // round-trips (SADD/KeyExpire/SetString) on the user-sessions index + main key; the fake
        // store has no secondary index and no round-trips to save, so it always slides — equivalent
        // to the shipped default SlideThreshold = 0.0. At any SlideThreshold the observable
        // ISessionStore contract (a live session authenticates; a revoked/deleted/expired one does
        // not) is identical; only the TTL-refresh cadence differs, which the fake does not expose.
        // Slide up to SlidingTtl, clamped so it never passes the absolute cap.
        var capRemaining = entry.CreatedAt + profile.AbsoluteTtl - now;
        var slidingTtl = capRemaining < profile.SlidingTtl ? capRemaining : profile.SlidingTtl;
        var newExpiry = now + slidingTtl;
        _sessions.TryUpdate(key, (entry.UserId, entry.CreatedAt, entry.RotatedAt, newExpiry, entry.Lifetime, entry.SupersededAt), entry);

        return Task.FromResult<Session?>(
            new Session(sessionId, entry.UserId, entry.CreatedAt, newExpiry, entry.Lifetime));
    }

    public Task<Session> CreateAsync(Guid userId, SessionLifetime lifetime, CancellationToken ct)
    {
        var sessionId = SessionId.Generate();
        var now = dateTimeProvider.UtcNow;
        var expiresAt = now + _options.ProfileFor(lifetime).SlidingTtl;

        // RotatedAt starts at CreatedAt (== now); SupersededAt null (live).
        _sessions[sessionId.Reveal()] = (userId, now, now, expiresAt, lifetime, null);

        return Task.FromResult(new Session(sessionId, userId, now, expiresAt, lifetime));
    }

    public Task<SessionRotation?> RotateAsync(SessionId current, CancellationToken ct)
    {
        var key = current.Reveal();
        if (!_sessions.TryGetValue(key, out var entry))
            return Task.FromResult<SessionRotation?>(null);

        // A superseded key is already rotated away (living out its grace) — never re-rotate.
        if (entry.SupersededAt is not null)
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

        // Single-winner election (mirrors the Redis SET NX claim): atomically transition the
        // old entry to superseded — the first caller to CAS wins; a loser gets null and keeps
        // using `current`. COND-A: the old id stays valid but non-sliding for RotationGraceWindow
        // (ExpiresAt = grace ceiling) so concurrent in-flight requests don't 401. The superseded
        // entry stays in the store so InvalidateAllForUserAsync (Art. 17) still reaches it.
        var graceExpiry = now + _options.RotationGraceWindow;
        var supersededEntry = (entry.UserId, entry.CreatedAt, now, graceExpiry, entry.Lifetime, (DateTimeOffset?)now);
        if (!_sessions.TryUpdate(key, supersededEntry, entry))
            return Task.FromResult<SessionRotation?>(null);

        var newId = SessionId.Generate();
        var capRemaining = entry.CreatedAt + profile.AbsoluteTtl - now;
        var slidingTtl = capRemaining < profile.SlidingTtl ? capRemaining : profile.SlidingTtl;
        var expiresAt = now + slidingTtl;

        // Preserve CreatedAt + Lifetime verbatim (cap anchor never resets); RotatedAt = now.
        _sessions[newId.Reveal()] = (entry.UserId, entry.CreatedAt, now, expiresAt, entry.Lifetime, (DateTimeOffset?)null);

        // COND-B fail-closed: a concurrent InvalidateAllForUserAsync tombstone means our new key
        // may outlive the revoke — undo (drop new AND the superseded old) and return null.
        if (IsRevoked(entry.UserId, now))
        {
            _sessions.TryRemove(newId.Reveal(), out _);
            _sessions.TryRemove(key, out _);
            return Task.FromResult<SessionRotation?>(null);
        }

        return Task.FromResult<SessionRotation?>(new SessionRotation(newId, expiresAt));
    }

    public Task<bool> InvalidateAsync(SessionId sessionId, CancellationToken ct)
        => Task.FromResult(_sessions.TryRemove(sessionId.Reveal(), out _));

    public Task<int> InvalidateAllForUserAsync(Guid userId, CancellationToken ct)
    {
        var now = dateTimeProvider.UtcNow;

        // COND-B: plant the tombstone BEFORE snapshotting so a concurrent RotateAsync whose new
        // key lands after our snapshot observes it and self-undoes (mirrors RedisSessionStore).
        _revoked[userId] = now + _options.RevocationTombstoneTtl;

        var toRemove = _sessions.Where(kv => kv.Value.UserId == userId).Select(kv => kv.Key).ToList();
        foreach (var key in toRemove)
            _sessions.TryRemove(key, out _);
        return Task.FromResult(toRemove.Count);
    }

    public Task MarkUserDeletedAsync(Guid userId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); // parity with RedisSessionStore (no other I/O to cancel)

        // PR2c-0 Layer 2 — plant the account-deletion tombstone (mirrors the Redis SET EX).
        // Idempotent; GetAsync fails closed while it is live (the 30-day restore window).
        _deleted[userId] = dateTimeProvider.UtcNow + _options.DeletionTombstoneTtl;
        return Task.CompletedTask;
    }

    private bool IsRevoked(Guid userId, DateTimeOffset now) =>
        _revoked.TryGetValue(userId, out var until) && until > now;

    private bool IsDeleted(Guid userId, DateTimeOffset now) =>
        _deleted.TryGetValue(userId, out var until) && until > now;
}
