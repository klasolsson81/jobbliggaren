using Jobbliggaren.Application.JobAds.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Application.JobAds.Jobs.Common;

/// <summary>
/// Emits the ADR 0045 Beslut 1 klass (d) ingestion-throughput signal. Shared by
/// both Platsbanken sync jobs (DRY — Hunt/Thomas 1999 — identical arithmetic
/// and byte-identical message templates so ONE Seq signal matches both jobs;
/// extracted per CTO bind #754 Q3, mirroring
/// <see cref="Jobbliggaren.Application.JobAds.Jobs.Common.JobAdRefetchBackfillRunner"/>'s
/// shared-core shape).
///
/// <para>
/// <b>The qualifying gate is ONE predicate:</b>
/// <c>fetched &gt;= MinItemsForVerdict AND durationSec &gt; 0</c>. A
/// non-qualifying run emits NOTHING — no <c>itemsPerMinute</c> field at all,
/// not even at Information. A logged rate is a claim; an unqualified rate
/// (e.g. a quiet 10-minute stream cron that fetched 3 items) would be a FALSE
/// claim that looks like an outage on a chart built six months from now (CTO
/// bind #754 Q3(iii)). The raw <c>fetched</c>/<c>durationSec</c> values are
/// already visible on the jobs' own <c>LogCompleted</c> events (5302/5402) for
/// anyone who wants a rate for a non-qualifying run, in full view of how small
/// the sample is.
/// </para>
///
/// <para>
/// <b>No separate "sustained" duration knob.</b> The floor and the sample-size
/// gate jointly derive sustained-ness — see
/// <see cref="IngestionThroughputOptions.MinItemsForVerdict"/> for the proof.
/// A <c>MinDurationSec</c> config knob would be provably inert (set ≤ 60s) or
/// harmful (set &gt; 60s, suppressing true positives) — not added.
/// </para>
///
/// <para>
/// <b>Numerator is <c>fetched</c>, not <c>added + updated</c>.</b>
/// <c>fetched</c> is pipeline throughput — what a capacity floor means. A
/// healthy nightly snapshot is dominated by <c>Skipped</c> (the snapshot is a
/// superset of what the stream already inserted), so <c>added + updated</c>
/// would warn on every healthy run — the exact false-alarm class this
/// instrument exists to avoid, merely relocated.
/// </para>
///
/// <para>
/// <b>The <c>durationSec == 0</c> guard is load-bearing, not decorative.</b>
/// Double division does not throw (<c>250/0.0 → +Infinity</c>,
/// <c>0/0.0 → NaN</c>), and <c>NaN &lt; 200</c> is <c>false</c> under IEEE
/// 754 — a NaN would silently FAIL to warn rather than crash. This is not
/// theoretical: a frozen <c>IDateTimeProvider</c> in a test (or a clock that
/// has not advanced between two reads) yields exactly
/// <c>startedAt == completedAt</c> → <c>durationSec == 0.0</c>.
/// </para>
/// </summary>
public sealed partial class IngestionThroughputReporter(
    IOptions<IngestionThroughputOptions> options,
    ILogger<IngestionThroughputReporter> logger)
{
    /// <summary>
    /// Reports one completed run. <paramref name="source"/>/<paramref name="jobType"/>
    /// mirror the values already carried on the <c>JobAdsSynced</c> audit row
    /// (e.g. "platsbanken" / "stream" or "snapshot"). Call ONLY after a run
    /// that completed without throwing — see the call-site comments in
    /// <c>SyncPlatsbankenStreamJob</c>/<c>SyncPlatsbankenSnapshotJob</c> for why
    /// (a crashed run has no valid capacity claim; a partial run would compute
    /// a bogus low rate and warn about capacity when the real event was a
    /// failure, already logged at Error).
    /// </summary>
    public void Report(string source, string jobType, int fetched, double durationSec)
    {
        try
        {
            var opts = options.Value;
            if (fetched < opts.MinItemsForVerdict || durationSec <= 0)
            {
                // Non-qualifying run: NO verdict, NO itemsPerMinute field anywhere.
                // Silence is the correct signal here — see class doc.
                return;
            }

            var itemsPerMinute = fetched * 60.0 / durationSec;
            LogThroughput(logger, source, jobType, fetched, durationSec, itemsPerMinute);

            if (itemsPerMinute < opts.FloorItemsPerMinute)
            {
                LogBelowFloor(logger, source, jobType, fetched, durationSec, itemsPerMinute, opts.FloorItemsPerMinute);
            }
        }
        catch (Exception ex)
        {
            // Stability invariant (CTO bind #754 Q1) — a telemetry component must never be
            // able to fault the process it monitors. The memory sampler got this guard twice
            // and the reporter got it zero times (dotnet-architect, #754).
            //
            // It is load-bearing HERE specifically: in SyncPlatsbankenSnapshotJob the call sits
            // between LogCompleted and auditor.RecordAsync, on a path with NO `finally`. MEL
            // aggregates provider exceptions and rethrows, and the Seq sink IS wired
            // (config-gated on Seq:ServerUrl) — so a sink failure here would drop the
            // JobAdsSynced audit row entirely AND fail the Hangfire job, forcing a full
            // ~47k-item re-run of an ingestion that actually SUCCEEDED.
            try
            {
                LogReportFailed(logger, ex);
            }
            catch (Exception)
            {
                // Terminal, and the whole point of nesting: if the SINK is what threw, the
                // handler that reports the failure throws for the same reason — an un-nested
                // guard would let the exception escape exactly as if there were no guard at
                // all, in precisely the scenario the guard exists for. You cannot log a logging
                // failure. Losing a throughput verdict is acceptable; losing the audit row and
                // re-running a successful 47k-item ingestion is not.
                // Pinned by Report_WhenTheLogSinkItselfThrows_DoesNotPropagate.
            }
        }
    }

    [LoggerMessage(EventId = 6203, Level = LogLevel.Warning,
        Message = "IngestionThroughputReporter: failed to emit the throughput verdict — the SYNC ITSELF SUCCEEDED, only this measurement was lost. See docs/runbooks/performance-measurement.md §C.")]
    private static partial void LogReportFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 6201, Level = LogLevel.Information,
        Message = "IngestionThroughput: source={Source}, jobType={JobType}, fetched={Fetched}, durationSec={DurationSec}, itemsPerMinute={ItemsPerMinute}.")]
    private static partial void LogThroughput(ILogger logger, string source, string jobType,
        int fetched, double durationSec, double itemsPerMinute);

    [LoggerMessage(EventId = 6202, Level = LogLevel.Warning,
        Message = "IngestionThroughputBelowFloor: source={Source}, jobType={JobType}, itemsPerMinute={ItemsPerMinute} < floor {FloorItemsPerMinute} (fetched={Fetched}, durationSec={DurationSec}). ADR 0045 Beslut 1 klass (d).")]
    private static partial void LogBelowFloor(ILogger logger, string source, string jobType,
        int fetched, double durationSec, double itemsPerMinute, int floorItemsPerMinute);
}
