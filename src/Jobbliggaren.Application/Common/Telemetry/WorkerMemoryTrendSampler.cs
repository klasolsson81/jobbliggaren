using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Application.Common.Telemetry;

/// <summary>
/// Worker memory trend sampler (ADR 0045 Beslut 3). Holds the below/above-cap
/// edge state across ticks and emits:
/// <list type="bullet">
///   <item>a per-tick Information trend event (always — so nothing is lost for
///     charting);</item>
///   <item>a Warning on the below→above transition — edge-triggered, not
///     per-tick, so a sustained breach does not produce 1440 Warning
///     events/day (CTO bind #754 Q4; ADR 0045 Beslut 5's "flaky perf-gate
///     sämre än ingen perf-gate" generalized to alarm fatigue);</item>
///   <item>an Information recovery event on the above→below transition.</item>
/// </list>
///
/// <para>
/// <b>Lifetime: singleton.</b> Holds edge state across ticks; called from
/// <c>WorkerMemoryTrendService</c> (Worker/Hosting), itself a singleton
/// <c>BackgroundService</c>. Its dependency chain is therefore singleton-safe
/// by construction: <see cref="IProcessMemoryProbe"/> is stateless,
/// <see cref="IOptions{TOptions}"/> and <see cref="ILogger{TCategoryName}"/>
/// are singleton-safe. No <c>IServiceScopeFactory</c>, no scoped dependency —
/// the Worker runs <c>ValidateScopes = true</c> and a scoped dependency here
/// would throw at container build (CTO bind #754 Q2).
/// </para>
///
/// <para>
/// <b>Honesty — process, not per-job.</b> <see cref="IProcessMemoryProbe"/>
/// measures the Worker PROCESS. With <c>WorkerCount = 4</c>, up to four
/// Hangfire jobs share the process, so there is no honest in-process
/// attribution to a single job instance. The trend event therefore carries NO
/// JobId/JobName field — do not add one without first solving the attribution
/// problem. Correlation to a specific job run happens at read time in Seq, by
/// time window, against the sync jobs' own start/complete events
/// (<c>docs/runbooks/performance-measurement.md</c> §B). See the dated ADR
/// 0045 Beslut 3 amendment for the full clarification.
/// </para>
///
/// <para>
/// <b>Not routed through Mediator — deliberately.</b> This is a plain sealed
/// class, not a Mediator message. <c>LoggingBehavior</c>'s
/// <c>Handled {MessageName} in {ElapsedMs}ms</c> event stream IS the
/// p95-per-handler dataset <c>docs/runbooks/performance-measurement.md</c> §A
/// aggregates — a synthetic 1/min sampler message would inject a fake row into
/// that table and corrupt the sibling instrument it is meant to complement
/// (CTO bind #754 Q2). It would also open a UnitOfWork for a read-only process
/// probe.
/// </para>
///
/// <para>
/// <b>Stability.</b> <see cref="Sample"/> never lets a probe or logging-sink
/// failure escape — a telemetry component must never be able to fault the
/// process it monitors (Nygard, <i>Release It!</i> 2nd ed. 2018; CTO bind
/// #754 Q1, non-negotiable). <c>WorkerMemoryTrendService</c> wraps the tick as
/// defense-in-depth on top of this.
/// </para>
/// </summary>
public sealed partial class WorkerMemoryTrendSampler(
    IProcessMemoryProbe probe,
    IOptions<WorkerMemoryTrendOptions> options,
    ILogger<WorkerMemoryTrendSampler> logger)
{
    // Edge state. Initialises false — a FIRST sample already above the cap is
    // therefore a false→true transition and MUST fire the warn (CTO bind
    // #754 Q4).
    private bool _aboveSoftCap;

    /// <summary>
    /// Takes one sample and emits the trend/edge events. Never throws — a
    /// probe or logging-sink failure is caught, logged at Warning, and
    /// swallowed here (the narrowest scope that touches the untrusted ambient
    /// API), so the caller can call this on a plain timer tick with no
    /// try/catch of its own required for correctness (though
    /// <c>WorkerMemoryTrendService</c> adds one anyway, as defense-in-depth).
    /// </summary>
    public void Sample()
    {
        try
        {
            var sample = probe.Sample();
            var cap = options.Value.SoftCapMiB;
            var workingSetMiB = sample.WorkingSetBytes / (1024.0 * 1024.0);

            LogTrend(logger, sample.WorkingSetBytes, sample.GcHeapBytes, sample.Gen2Collections);

            var isAboveCap = workingSetMiB > cap;
            if (isAboveCap && !_aboveSoftCap)
            {
                LogAboveSoftCap(logger, sample.WorkingSetBytes, cap);
            }
            else if (!isAboveCap && _aboveSoftCap)
            {
                LogBackWithinSoftCap(logger, sample.WorkingSetBytes, cap);
            }

            _aboveSoftCap = isAboveCap;
        }
        catch (Exception ex)
        {
            // Stability invariant (CTO bind #754 Q1) — a telemetry component
            // must never be able to fault the process it monitors. A probe or
            // sink failure is logged at Warning and swallowed; the edge state
            // is left unchanged (we do not know the true cap state from a
            // failed sample, so we do not guess one).
            LogSampleFailed(logger, ex);
        }
    }

    [LoggerMessage(EventId = 6210, Level = LogLevel.Information,
        Message = "WorkerMemoryTrend: workingSetBytes={WorkingSetBytes}, gcHeapBytes={GcHeapBytes}, gen2Collections={Gen2Collections}.")]
    private static partial void LogTrend(ILogger logger, long workingSetBytes, long gcHeapBytes, int gen2Collections);

    [LoggerMessage(EventId = 6211, Level = LogLevel.Warning,
        Message = "WorkerMemoryAboveSoftCap: workingSetBytes={WorkingSetBytes} exceeds the ADR 0045 Beslut 3 soft cap ({SoftCapMiB} MiB). Observe-only Fas 1 — see docs/runbooks/performance-measurement.md §B.")]
    private static partial void LogAboveSoftCap(ILogger logger, long workingSetBytes, int softCapMiB);

    [LoggerMessage(EventId = 6212, Level = LogLevel.Information,
        Message = "WorkerMemoryBackWithinSoftCap: workingSetBytes={WorkingSetBytes} back within the ADR 0045 Beslut 3 soft cap ({SoftCapMiB} MiB).")]
    private static partial void LogBackWithinSoftCap(ILogger logger, long workingSetBytes, int softCapMiB);

    [LoggerMessage(EventId = 6213, Level = LogLevel.Warning,
        Message = "WorkerMemoryTrendSampler: probe or logging-sink failure during Sample() — this tick is skipped, the host continues.")]
    private static partial void LogSampleFailed(ILogger logger, Exception exception);
}
