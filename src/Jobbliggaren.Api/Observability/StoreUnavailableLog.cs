using System.Collections.Concurrent;
using System.Diagnostics;

namespace Jobbliggaren.Api.Observability;

/// <summary>
/// Emits the store-unavailable Error log (#512, epic #484) on the one deliberately-handled
/// infrastructure path — the Program.cs middleware that maps a
/// <c>StoreUnavailableException</c> to 503. Auth runs outside the Mediator pipeline, so
/// <c>LoggingBehavior</c> never sees a session-store failure; without this log a Redis outage
/// produces ZERO signal, and the planned #1172 5xx alarm has nothing to alarm on.
/// </summary>
/// <remarks>
/// <para>
/// Coarsely throttled: a Redis outage makes EVERY request on that store take the 503 path, so an
/// unthrottled Error log would flood the sink with identical entries. At most one entry per
/// <see cref="ThrottleWindow"/> is enough for an operator/alarm to detect the outage. The window
/// is measured with <see cref="Stopwatch.GetTimestamp"/> (a MONOTONIC counter, not wall-clock —
/// so it needs no <c>IDateTimeProvider</c> and a clock change cannot skew it; §5 forbids
/// <c>DateTime.UtcNow</c>, not monotonic elapsed-time measurement).
/// </para>
/// <para>
/// <b>One window PER STORE (#1735).</b> The session store and the volatile Redis are separate
/// instances and fail independently; one shared window would let an outage of either hide the
/// other's first entry.
/// </para>
/// <para>
/// Registered as a singleton so the throttle windows are shared across all requests of one host.
/// Kept out of the decorator so the resilience seam stays log-free (one log site, no double-log).
/// </para>
/// </remarks>
public sealed partial class StoreUnavailableLog(ILogger<StoreUnavailableLog> logger)
{
    private static readonly TimeSpan ThrottleWindow = TimeSpan.FromSeconds(10);

    // Keyed by StoreUnavailableException.Store. An absent key = never emitted for that store.
    private readonly ConcurrentDictionary<string, long> _lastEmitTimestamps = new(StringComparer.Ordinal);

    /// <summary>
    /// Logs the outage once per throttle window and store. Takes the failure's TYPE NAME, never the
    /// exception: see <see cref="LogUnavailable"/>.
    /// </summary>
    public void Emit(string store, string innerType)
    {
        var now = Stopwatch.GetTimestamp();
        if (_lastEmitTimestamps.TryGetValue(store, out var last)
            && Stopwatch.GetElapsedTime(last, now) < ThrottleWindow)
            return;

        // Race-tolerant coarse valve: if two threads pass the gate at once, both may log once —
        // acceptable, because the only outcome we must never allow is MISSING the first log.
        _lastEmitTimestamps[store] = now;
        LogUnavailable(store, innerType);
    }

    // §5 / GDPR Art. 5(1)(c) data-minimisation: log ONLY the dedicated event-id, the store and the
    // inner Redis exception's TYPE (connection vs timeout vs server — the degradation class the
    // #1172 alarm keys on). The exception MESSAGE is deliberately NOT logged: StackExchange.Redis
    // embeds the operated key in the message (IncludeDetailInExceptions defaults true), and a
    // user-keyed op's key carries the raw userId Guid (a pseudonymous identifier), an
    // address-derived one a fingerprint of the address. The raw session token can never appear
    // anyway (its Redis key is a SHA-256 hash), but dropping the message removes those paths too.
    // event_name= convention feeds the alarm metric filter (ADR 0031/0036).
    [LoggerMessage(
        EventId = 2050,
        Level = LogLevel.Error,
        Message = "event_name=store_unavailable store={Store} inner_type={InnerType} — svarar 503.")]
    private partial void LogUnavailable(string store, string innerType);
}
