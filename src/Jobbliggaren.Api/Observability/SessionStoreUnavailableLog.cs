using System.Diagnostics;

namespace Jobbliggaren.Api.Observability;

/// <summary>
/// Emits the session-store-unavailable Error log (#512, epic #484) on the one deliberately-handled
/// infrastructure path — the Program.cs middleware that maps
/// <c>SessionStoreUnavailableException</c> to 503. Auth runs outside the Mediator pipeline, so
/// <c>LoggingBehavior</c> never sees this failure; without this log a Redis outage produces ZERO
/// signal, and the planned TD-77 5xx alarm has nothing to alarm on.
/// </summary>
/// <remarks>
/// <para>
/// Coarsely throttled: a Redis outage makes EVERY authenticated request take the 503 path, so an
/// unthrottled Error log would flood the sink with identical entries. At most one entry per
/// <see cref="ThrottleWindow"/> is enough for an operator/alarm to detect the outage. The window
/// is measured with <see cref="Stopwatch.GetTimestamp"/> (a MONOTONIC counter, not wall-clock —
/// so it needs no <c>IDateTimeProvider</c> and a clock change cannot skew it; §5 forbids
/// <c>DateTime.UtcNow</c>, not monotonic elapsed-time measurement).
/// </para>
/// <para>
/// Registered as a singleton so the throttle window is shared across all requests of one host.
/// Kept out of the decorator so the resilience seam stays log-free (one log site, no double-log).
/// </para>
/// </remarks>
public sealed partial class SessionStoreUnavailableLog(ILogger<SessionStoreUnavailableLog> logger)
{
    private static readonly TimeSpan ThrottleWindow = TimeSpan.FromSeconds(10);

    // 0 = never emitted (Stopwatch.GetTimestamp is always positive). Guarded by Interlocked.
    private long _lastEmitTimestamp;

    /// <summary>
    /// Logs the outage once per throttle window. <paramref name="inner"/> is the original Redis
    /// exception (connection / timeout / server); only its TYPE is logged — the message is dropped
    /// for data-minimisation (see <see cref="LogUnavailable"/>).
    /// </summary>
    public void Emit(Exception inner)
    {
        var now = Stopwatch.GetTimestamp();
        var last = Interlocked.Read(ref _lastEmitTimestamp);
        if (last != 0 && Stopwatch.GetElapsedTime(last, now) < ThrottleWindow)
            return;

        // Race-tolerant coarse valve: if two threads pass the gate at once, both may log once —
        // acceptable, because the only outcome we must never allow is MISSING the first log.
        Interlocked.Exchange(ref _lastEmitTimestamp, now);
        LogUnavailable(inner.GetType().Name);
    }

    // §5 / GDPR Art. 5(1)(c) data-minimisation: log ONLY the dedicated event-id + the inner Redis
    // exception's TYPE (connection vs timeout vs server — the degradation class the TD-77 alarm
    // keys on). The exception MESSAGE is deliberately NOT logged: StackExchange.Redis embeds the
    // operated key in the message (IncludeDetailInExceptions defaults true), and a user-keyed op's
    // key carries the raw userId Guid (a pseudonymous identifier). The raw session token can never
    // appear anyway (its Redis key is a SHA-256 hash), but dropping the message removes the userId
    // path too. event_name= convention feeds the alarm metric filter (ADR 0031/0036).
    [LoggerMessage(
        EventId = 2050,
        Level = LogLevel.Error,
        Message = "event_name=session_store_unavailable inner_type={InnerType} " +
                  "— session-store (Redis) otillgänglig, svarar 503. Throttlad; TD-77 5xx-alarmsignal.")]
    private partial void LogUnavailable(string innerType);
}
