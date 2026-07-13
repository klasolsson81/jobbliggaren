using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Jobs.Common;
using Jobbliggaren.Application.UnitTests.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Jobs.Common;

/// <summary>
/// ADR 0045 Beslut 1 klass (d) — ingestion-throughput fitness function. Tested
/// here in isolation, directly against the REAL shipped defaults (200/200 —
/// CTO bind #754 Q3), independent of either sync job.
/// </summary>
public class IngestionThroughputReporterTests
{
    private static IngestionThroughputReporter CreateReporter(
        RecordingLogger<IngestionThroughputReporter>? recorder = null,
        IngestionThroughputOptions? options = null) =>
        new(Options.Create(options ?? new IngestionThroughputOptions()),
            recorder ?? new RecordingLogger<IngestionThroughputReporter>());

    [Fact]
    public void Defaults_MatchAdr0045Beslut1ClassD()
    {
        // Guards against silent drift of the shipped default — every test
        // below runs against THESE values, never lowered ones.
        var opts = new IngestionThroughputOptions();

        opts.FloorItemsPerMinute.ShouldBe(200);
        opts.MinItemsForVerdict.ShouldBe(200);
    }

    [Fact]
    public void Report_QualifyingRunBelowFloor_EmitsWarning()
    {
        var recorder = new RecordingLogger<IngestionThroughputReporter>();
        var reporter = CreateReporter(recorder);

        // 300 fetched over 300s = 60/min < 200 floor. Also qualifies
        // (300 >= 200 MinItemsForVerdict).
        reporter.Report("platsbanken", "stream", fetched: 300, durationSec: 300);

        recorder.Records.ShouldContain(r => r.EventId.Id == 6202 && r.Level == LogLevel.Warning);
    }

    [Fact]
    public void Report_QualifyingRunAtFloor_DoesNotWarn()
    {
        var recorder = new RecordingLogger<IngestionThroughputReporter>();
        var reporter = CreateReporter(recorder);

        // 200 fetched over 60s = exactly 200/min = the floor, not BELOW it.
        reporter.Report("platsbanken", "stream", fetched: 200, durationSec: 60);

        recorder.Records.ShouldNotContain(r => r.EventId.Id == 6202);
        recorder.Records.ShouldContain(r => r.EventId.Id == 6201, "an at-floor run still qualifies and gets a trend event");
    }

    [Fact]
    public void Report_QualifyingRunAboveFloor_DoesNotWarn()
    {
        var recorder = new RecordingLogger<IngestionThroughputReporter>();
        var reporter = CreateReporter(recorder);

        // 47000 fetched over 40min (2400s) ~= 1175/min — a healthy nightly snapshot.
        reporter.Report("platsbanken", "snapshot", fetched: 47_000, durationSec: 2400);

        recorder.Records.ShouldContain(r => r.EventId.Id == 6201 && r.Level == LogLevel.Information);
        recorder.Records.ShouldNotContain(r => r.EventId.Id == 6202);
    }

    [Fact]
    public void Report_NonQualifyingRun_BelowMinItems_EmitsNothingAtAll()
    {
        // The false-alarm guard — a quiet 10-minute stream cron that fetched
        // 3 items must NOT get an itemsPerMinute field at all (CTO bind #754
        // Q3(iii) — a logged rate from too small a sample is a false claim).
        var recorder = new RecordingLogger<IngestionThroughputReporter>();
        var reporter = CreateReporter(recorder);

        reporter.Report("platsbanken", "stream", fetched: 3, durationSec: 5);

        recorder.Records.ShouldBeEmpty();
    }

    [Fact]
    public void Report_NonQualifyingRun_OneBelowMinItemsThreshold_EmitsNothing()
    {
        // Boundary: 199 < 200 MinItemsForVerdict — must NOT qualify, even
        // though the raw rate (199 fetched / 1s = huge/min) would look fine.
        var recorder = new RecordingLogger<IngestionThroughputReporter>();
        var reporter = CreateReporter(recorder);

        reporter.Report("platsbanken", "stream", fetched: 199, durationSec: 1);

        recorder.Records.ShouldBeEmpty();
    }

    [Fact]
    public void Report_DurationZero_EmitsNothingRatherThanInfinityOrNaN()
    {
        // A frozen IDateTimeProvider (startedAt == completedAt) yields exactly
        // durationSec == 0 in a caller. Double division does not throw
        // (250/0.0 -> +Infinity), and NaN < 200 is FALSE under IEEE 754 — a
        // NaN would silently fail to warn rather than crash. The guard must
        // therefore emit nothing at all, same as any other non-qualifying run
        // (CTO bind #754 Q3(ii)).
        var recorder = new RecordingLogger<IngestionThroughputReporter>();
        var reporter = CreateReporter(recorder);

        reporter.Report("platsbanken", "snapshot", fetched: 47_000, durationSec: 0);

        recorder.Records.ShouldBeEmpty();
    }

    [Fact]
    public void Report_NegativeDuration_EmitsNothing()
    {
        // Defensive superset of the durationSec==0 guard — a non-monotonic
        // clock should never produce a warn either.
        var recorder = new RecordingLogger<IngestionThroughputReporter>();
        var reporter = CreateReporter(recorder);

        reporter.Report("platsbanken", "snapshot", fetched: 47_000, durationSec: -5);

        recorder.Records.ShouldBeEmpty();
    }

    [Fact]
    public void Report_QualifyingRun_LogsSourceAndJobTypeAndFetched()
    {
        var recorder = new RecordingLogger<IngestionThroughputReporter>();
        var reporter = CreateReporter(recorder);

        reporter.Report("platsbanken", "stream", fetched: 600, durationSec: 60);

        // Field-anchored (not bare substring) so a source/jobType argument
        // swap at the call site is actually caught — "platsbanken" and
        // "stream" alone would both still be present in the message either
        // way, just in the wrong field.
        var record = recorder.Records.Single(r => r.EventId.Id == 6201);
        record.Message.ShouldContain("source=platsbanken");
        record.Message.ShouldContain("jobType=stream");
        record.Message.ShouldContain("fetched=600");
    }

    [Fact]
    public void Report_WhenTheLogSinkItselfThrows_DoesNotPropagate()
    {
        // The reporter is called from SyncPlatsbankenSnapshotJob BETWEEN LogCompleted and
        // auditor.RecordAsync, on a path with NO `finally`. An exception escaping here would
        // drop the JobAdsSynced audit row AND fail the Hangfire job — forcing a full ~47k-item
        // re-run of an ingestion that actually SUCCEEDED. A throughput verdict is never worth
        // that (dotnet-architect, #754).
        //
        // The sink is the realistic thing to fail, which is why a NESTED guard is required: an
        // un-nested `catch { LogReportFailed(logger, ex); }` would itself throw here, and the
        // exception would escape exactly as if the guard were absent.
        var reporter = new IngestionThroughputReporter(
            Options.Create(new IngestionThroughputOptions()),
            new ThrowingSinkLogger<IngestionThroughputReporter>());

        // A qualifying run — the only kind that reaches a log call at all.
        Should.NotThrow(() => reporter.Report("platsbanken", "snapshot", fetched: 47_000, durationSec: 2400));
    }
}
