using Jobbliggaren.Application.Common.Abstractions;
using StackExchange.Redis;

namespace Jobbliggaren.Infrastructure.Auth.Sessions;

/// <summary>
/// Resilience decorator around <see cref="ISessionStore"/> (senior-cto-advisor Variant C,
/// 2026-07-10; #511, epic #484). It translates the two degraded-Redis states the inner
/// <see cref="RedisSessionStore"/> does NOT already wrap — <see cref="RedisTimeoutException"/>
/// (Redis slow/overloaded) and <see cref="RedisServerException"/> (LOADING during an RDB
/// restart) — into the technology-neutral <see cref="SessionStoreUnavailableException"/>
/// contract, so the Api exception-mapping middleware renders a 503 instead of leaking an
/// unhandled 500. Without this, those two exceptions escape the store raw and become the most
/// common source of spurious 500s during a Redis blip.
/// </summary>
/// <remarks>
/// <para>
/// WHY a decorator rather than widening the catches inside <see cref="RedisSessionStore"/>:
/// that file is owned by a concurrent lane (CLAUDE.md §6.5 hotspot) AND — more fundamentally —
/// this keeps the Redis exception knowledge inside Infrastructure while leaving the Api pipeline
/// technology-neutral (§2.1); catching Redis types in Program.cs was rejected as a boundary leak.
/// The inner store already wraps <see cref="RedisConnectionException"/>, so this decorator closes
/// only the remaining two states. The split is internal: both arms yield the SAME contract
/// exception, so callers still see one uniform 503 signal.
/// </para>
/// <para>
/// The happy path is a pure pass-through — no extra Redis round-trip, so the auth hot path keeps
/// its ADR 0045 budget. The decorator deliberately does NOT log: the single 503 log site lives in
/// the Api middleware (#512), which keeps this class free of a logging dependency and avoids
/// double-logging one outage.
/// </para>
/// </remarks>
public sealed class SessionStoreResilienceDecorator(ISessionStore inner) : ISessionStore
{
    public Task<Session?> GetAsync(SessionId sessionId, CancellationToken ct) =>
        Guard(() => inner.GetAsync(sessionId, ct));

    public Task<Session> CreateAsync(Guid userId, SessionLifetime lifetime, CancellationToken ct) =>
        Guard(() => inner.CreateAsync(userId, lifetime, ct));

    public Task<SessionRotation?> RotateAsync(SessionId current, CancellationToken ct) =>
        Guard(() => inner.RotateAsync(current, ct));

    public Task<bool> InvalidateAsync(SessionId sessionId, CancellationToken ct) =>
        Guard(() => inner.InvalidateAsync(sessionId, ct));

    public Task<int> InvalidateAllForUserAsync(Guid userId, CancellationToken ct) =>
        Guard(() => inner.InvalidateAllForUserAsync(userId, ct));

    public Task MarkUserDeletedAsync(Guid userId, CancellationToken ct) =>
        // The void arm delegates to the single generic Guard (DRY — one translation site) by
        // returning a discarded sentinel; the resulting task satisfies the non-generic Task return.
        Guard<object?>(async () =>
        {
            await inner.MarkUserDeletedAsync(userId, ct);
            return null;
        });

    /// <summary>
    /// The single fault-translation site (DRY): the knowledge of WHICH Redis exceptions mean
    /// "degraded → 503" lives here, once, for all six port methods.
    /// </summary>
    /// <remarks>
    /// <see cref="RedisTimeoutException"/> does NOT derive from <see cref="RedisException"/> — it
    /// derives from <see cref="TimeoutException"/> — so a naive <c>catch (RedisException)</c> would
    /// MISS the single most common degraded state; both are listed explicitly. An already-translated
    /// <see cref="SessionStoreUnavailableException"/> (the inner store's
    /// <see cref="RedisConnectionException"/> arm) passes straight through, never double-wrapped.
    /// </remarks>
    private static async Task<T> Guard<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation();
        }
        catch (SessionStoreUnavailableException)
        {
            throw;
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            throw new SessionStoreUnavailableException("Redis-session-store är inte tillgänglig.", ex);
        }
    }
}
