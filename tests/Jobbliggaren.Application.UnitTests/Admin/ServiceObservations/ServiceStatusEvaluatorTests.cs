using Jobbliggaren.Application.Admin.ServiceObservations;
using Jobbliggaren.Application.Admin.ServiceObservations.Queries.GetServiceObservation;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.ServiceObservations;

/// <summary>
/// #1982 (ADR 0159) — what each service row may claim, as a function of the snapshot and of "now". The
/// evaluator is pure, so every case states the clock it is judged at. The limits are written as numbers in the
/// boundary tests on purpose: they are the contract the page words its text from.
///
/// <para>
/// Which states a real system can produce, per AGENTS.md §5 (Tests). A heartbeat or a sample dated later than the
/// API's own clock: the actor is a backward step of the host's wall clock between the write and the read (the one
/// <see cref="ServiceObservationLimits.FutureTolerance"/> names); inside the tolerance it is read as young, beyond it
/// as nothing.
/// </para>
/// </summary>
public sealed class ServiceStatusEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly ServiceObservationOptions Options = new();

    private static ServiceObservationSnapshot Snapshot(
        SourceSample<WorkerObservation>? worker = null,
        SourceSample<bool>? store = null,
        SourceSample<ImportObservation>? import = null) => new(
            worker ?? SourceSample.Missing<WorkerObservation>(),
            store ?? SourceSample.Missing<bool>(),
            import ?? SourceSample.Missing<ImportObservation>());

    /// <summary>
    /// A Worker sample taken <paramref name="sampleAge"/> before "now", by a sampler whose first successful read of the
    /// Worker source was <paramref name="watchedFor"/> before that sample.
    /// </summary>
    private static SourceSample<WorkerObservation> Worker(
        bool hasRecords, TimeSpan? heartbeatAge, TimeSpan? sampleAge = null, TimeSpan? watchedFor = null)
    {
        var sampledAt = Now - (sampleAge ?? TimeSpan.FromSeconds(10));
        return SourceSample.Of(
            sampledAt,
            new WorkerObservation(
                hasRecords,
                heartbeatAge is { } age ? Now - age : null,
                sampledAt - (watchedFor ?? TimeSpan.FromHours(1))));
    }

    private static SourceSample<ImportObservation> Import(TimeSpan? runAge, int errors = 0, TimeSpan? sampleAge = null) =>
        SourceSample.Of(
            Now - (sampleAge ?? TimeSpan.FromSeconds(10)),
            new ImportObservation(runAge is { } age ? new StreamImportRun(Now - age, errors) : null));

    private static ServiceObservationDto Evaluate(ServiceObservationSnapshot snapshot) =>
        ServiceStatusEvaluator.Evaluate(snapshot, Now, Options);

    // ── The limits the page words itself from ──────────────────────────────────────────────

    [Fact]
    public void Evaluate_CarriesTheThreeLimitsAsNumbers()
    {
        var dto = Evaluate(Snapshot());

        (dto.StaleAfterSeconds, dto.WorkerAliveWithinSeconds, dto.ImportOverdueAfterSeconds).ShouldBe((120, 120, 1500));
        dto.ReadAt.ShouldBe(Now);
    }

    // ── Worker ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Worker_ASignOfLifeInsideTheWindow_IsAliveWithTheHeartbeatAndTheSampleTime()
    {
        var dto = Evaluate(Snapshot(worker: Worker(hasRecords: true, TimeSpan.FromSeconds(40))));

        dto.BackgroundJobs.State.ShouldBe(WorkerProcessState.Alive);
        dto.BackgroundJobs.HeartbeatAt.ShouldBe(Now.AddSeconds(-40));
        dto.BackgroundJobs.SampledAt.ShouldBe(Now.AddSeconds(-10));
    }

    [Theory]
    [InlineData(120, WorkerProcessState.Alive)]
    [InlineData(121, WorkerProcessState.Silent)]
    public void Worker_TheAliveWindowEndsAfterExactlyOneHundredAndTwentySeconds(int heartbeatAgeSeconds, WorkerProcessState expected)
    {
        var dto = Evaluate(Snapshot(worker: Worker(hasRecords: true, TimeSpan.FromSeconds(heartbeatAgeSeconds))));

        dto.BackgroundJobs.State.ShouldBe(expected);
        dto.BackgroundJobs.HeartbeatAt.ShouldBe(Now.AddSeconds(-heartbeatAgeSeconds));
    }

    [Fact]
    public void Worker_ARecordWithAnOldHeartbeat_IsSilentAndStillSaysWhenItWasLastSeen()
    {
        var dto = Evaluate(Snapshot(worker: Worker(hasRecords: true, TimeSpan.FromMinutes(7))));

        dto.BackgroundJobs.State.ShouldBe(WorkerProcessState.Silent);
        dto.BackgroundJobs.HeartbeatAt.ShouldBe(Now.AddMinutes(-7));
    }

    [Fact]
    public void Worker_ASilentRecord_NeedsNoWatching_BecauseARecordIsSeenNotInferred()
    {
        // The sampler read the storage once, a moment ago, and the record it saw is old: that is Silent at once.
        var dto = Evaluate(Snapshot(worker: Worker(hasRecords: true, TimeSpan.FromMinutes(7), watchedFor: TimeSpan.Zero)));

        dto.BackgroundJobs.State.ShouldBe(WorkerProcessState.Silent);
    }

    [Fact]
    public void Worker_NoRecordAndOnlyOldEvidence_IsNoServerOnceTheSamplerHasWatchedAsLongAsTheWindow()
    {
        // The record is gone (a graceful stop removes it) and the last sign of life is older than the window.
        var dto = Evaluate(Snapshot(worker: Worker(hasRecords: false, TimeSpan.FromMinutes(3), watchedFor: TimeSpan.FromMinutes(5))));

        dto.BackgroundJobs.State.ShouldBe(WorkerProcessState.NoServer);
        dto.BackgroundJobs.HeartbeatAt.ShouldBeNull();
    }

    [Fact]
    public void Worker_NoRecordAndOnlyOldEvidence_IsUnknownBeforeTheSamplerHasWatchedAsLongAsTheWindow()
    {
        // The same absence, thirty seconds into watching: a release in progress reads the same, so nothing is claimed.
        var dto = Evaluate(Snapshot(worker: Worker(hasRecords: false, TimeSpan.FromMinutes(3), watchedFor: TimeSpan.FromSeconds(30))));

        dto.BackgroundJobs.State.ShouldBe(WorkerProcessState.Unknown);
    }

    [Fact]
    public void Worker_NoRecordButARecentSignOfLife_IsStillAliveBecauseItIsTheLastSignAndSaysWhen()
    {
        // The record was removed by a graceful stop 20 s ago: "last sign of life 20 s ago" is true, and no more is claimed.
        var dto = Evaluate(Snapshot(worker: Worker(hasRecords: false, TimeSpan.FromSeconds(20), watchedFor: TimeSpan.FromSeconds(25))));

        dto.BackgroundJobs.State.ShouldBe(WorkerProcessState.Alive);
        dto.BackgroundJobs.HeartbeatAt.ShouldBe(Now.AddSeconds(-20));
    }

    [Theory]
    [InlineData(60, WorkerProcessState.Unknown)]
    [InlineData(119, WorkerProcessState.Unknown)]
    [InlineData(120, WorkerProcessState.NoServer)]
    [InlineData(3600, WorkerProcessState.NoServer)]
    public void Worker_NoRecordAndNoEvidenceAtAll_IsNoServerOnlyOnceTheSamplerHasWatchedAsLongAsTheAliveWindow(
        int watchedForSeconds, WorkerProcessState expected)
    {
        var dto = Evaluate(Snapshot(
            worker: Worker(hasRecords: false, heartbeatAge: null, watchedFor: TimeSpan.FromSeconds(watchedForSeconds))));

        dto.BackgroundJobs.State.ShouldBe(expected);
    }

    [Fact]
    public void Worker_TheWatchedWindowIsMeasuredAtTheSampleNotAtTheRead()
    {
        // A sample taken when the sampler had watched for 90 s, read 30 s later (the next tick not yet published):
        // at the read 120 s have passed, but the sample was taken before the window was full.
        var dto = Evaluate(Snapshot(worker: Worker(
            hasRecords: false, heartbeatAge: null, sampleAge: TimeSpan.FromSeconds(30), watchedFor: TimeSpan.FromSeconds(90))));

        dto.BackgroundJobs.State.ShouldBe(WorkerProcessState.Unknown);
    }

    // Actor: a backward step of the host's wall clock between the heartbeat's write and this read. Inside the five-second
    // tolerance the heartbeat is young; beyond it, it is not evidence.
    [Theory]
    [InlineData(-4, WorkerProcessState.Alive)]
    [InlineData(-6, WorkerProcessState.Unknown)]
    public void Worker_AHeartbeatFromTheFuture_IsUnknownBeyondTheFiveSecondTolerance(
        int ageSeconds, WorkerProcessState expected)
    {
        var dto = Evaluate(Snapshot(worker: Worker(hasRecords: true, TimeSpan.FromSeconds(ageSeconds))));

        dto.BackgroundJobs.State.ShouldBe(expected);
    }

    // ── A sample too old to judge, missing, or from the future is withdrawn, never shown as an old verdict ──

    [Theory]
    [InlineData(120, true)]
    [InlineData(121, false)]
    public void AnySource_ASampleOlderThanTheStaleLimit_IsUnknownAndCarriesNothing(int sampleAgeSeconds, bool judged)
    {
        var age = TimeSpan.FromSeconds(sampleAgeSeconds);
        var snapshot = Snapshot(
            worker: Worker(hasRecords: true, TimeSpan.FromSeconds(30), age),
            store: SourceSample.Of(Now - age, true),
            import: Import(TimeSpan.FromMinutes(2), sampleAge: age));

        var dto = Evaluate(snapshot);

        (dto.BackgroundJobs.State != WorkerProcessState.Unknown).ShouldBe(judged);
        (dto.VolatileStore.State != VolatileStoreState.Unknown).ShouldBe(judged);
        (dto.JobImport.State != StreamImportState.Unknown).ShouldBe(judged);
        if (!judged)
        {
            dto.BackgroundJobs.SampledAt.ShouldBeNull();
            dto.BackgroundJobs.HeartbeatAt.ShouldBeNull();
            dto.VolatileStore.SampledAt.ShouldBeNull();
            dto.JobImport.SampledAt.ShouldBeNull();
            dto.JobImport.LastRunAt.ShouldBeNull();
            dto.JobImport.Errors.ShouldBeNull();
        }
    }

    [Theory]
    [InlineData(40, true)]
    [InlineData(46, false)]
    public void AnySource_TheStaleLimitIsTheConfiguredOne_NotTheDefault(int sampleAgeSeconds, bool judged)
    {
        var age = TimeSpan.FromSeconds(sampleAgeSeconds);
        var snapshot = Snapshot(
            worker: Worker(hasRecords: true, TimeSpan.FromSeconds(30), age),
            store: SourceSample.Of(Now - age, true),
            import: Import(TimeSpan.FromMinutes(2), sampleAge: age));

        var dto = ServiceStatusEvaluator.Evaluate(
            snapshot, Now, new ServiceObservationOptions { SampleIntervalSeconds = 10, StaleAfterSeconds = 45 });

        dto.StaleAfterSeconds.ShouldBe(45);
        (dto.BackgroundJobs.State != WorkerProcessState.Unknown).ShouldBe(judged);
        (dto.VolatileStore.State != VolatileStoreState.Unknown).ShouldBe(judged);
        (dto.JobImport.State != StreamImportState.Unknown).ShouldBe(judged);
    }

    [Fact]
    public void AnySource_NotSampledYet_IsUnknownInAllThreeRows()
    {
        var dto = Evaluate(ServiceObservationSnapshot.NotSampled());

        dto.BackgroundJobs.State.ShouldBe(WorkerProcessState.Unknown);
        dto.VolatileStore.State.ShouldBe(VolatileStoreState.Unknown);
        dto.JobImport.State.ShouldBe(StreamImportState.Unknown);
    }

    // Actor: a backward step of the host's wall clock between the sample's stamp and this read.
    [Fact]
    public void AnySource_ASampleFromTheFuture_IsUnknown()
    {
        var dto = Evaluate(Snapshot(
            worker: Worker(hasRecords: true, TimeSpan.FromSeconds(30), sampleAge: TimeSpan.FromSeconds(-6)),
            store: SourceSample.Of(Now.AddSeconds(6), true),
            import: Import(TimeSpan.FromMinutes(2), sampleAge: TimeSpan.FromSeconds(-6))));

        dto.BackgroundJobs.State.ShouldBe(WorkerProcessState.Unknown);
        dto.VolatileStore.State.ShouldBe(VolatileStoreState.Unknown);
        dto.JobImport.State.ShouldBe(StreamImportState.Unknown);
    }

    // ── Volatile store ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(true, VolatileStoreState.Reachable)]
    [InlineData(false, VolatileStoreState.Unreachable)]
    public void VolatileStore_TheVerdictOfTheCheck_IsCarriedWithTheTimeItWasRun(bool reachable, VolatileStoreState expected)
    {
        var dto = Evaluate(Snapshot(store: SourceSample.Of(Now.AddSeconds(-25), reachable)));

        dto.VolatileStore.State.ShouldBe(expected);
        dto.VolatileStore.SampledAt.ShouldBe(Now.AddSeconds(-25));
    }

    // ── Import ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Import_NothingRecordedInTheWindow_IsNoneRecordedWithNoRunAndNoCount()
    {
        var dto = Evaluate(Snapshot(import: Import(runAge: null)));

        dto.JobImport.State.ShouldBe(StreamImportState.NoneRecorded);
        dto.JobImport.SampledAt.ShouldBe(Now.AddSeconds(-10));
        dto.JobImport.LastRunAt.ShouldBeNull();
        dto.JobImport.Errors.ShouldBeNull();
    }

    [Theory]
    [InlineData(1, StreamImportState.Recorded)]
    [InlineData(1500, StreamImportState.Recorded)] // 25 minutes: two missed runs of the */10 cron and five minutes
    [InlineData(1501, StreamImportState.Overdue)]
    [InlineData(23 * 3600, StreamImportState.Overdue)]
    public void Import_TheOverdueLimitIsTwentyFiveMinutes(int runAgeSeconds, StreamImportState expected)
    {
        var dto = Evaluate(Snapshot(import: Import(TimeSpan.FromSeconds(runAgeSeconds), errors: 3)));

        dto.JobImport.State.ShouldBe(expected);
        dto.JobImport.LastRunAt.ShouldBe(Now.AddSeconds(-runAgeSeconds));
        dto.JobImport.Errors.ShouldBe(3);
    }

    [Fact]
    public void Import_ARecordedRunWithNoFailedEvents_CarriesAZeroTheCardNeverPrints()
    {
        // Zero is a number the wire carries; that the page does not word it is the page's rule (and the ADR's).
        var dto = Evaluate(Snapshot(import: Import(TimeSpan.FromMinutes(4), errors: 0)));

        dto.JobImport.State.ShouldBe(StreamImportState.Recorded);
        dto.JobImport.Errors.ShouldBe(0);
    }

    // Actor: a backward step of the host's wall clock between the run's audit row and this read.
    [Fact]
    public void Import_ARunThatFinishedInTheFuture_IsUnknown()
    {
        var dto = Evaluate(Snapshot(import: Import(TimeSpan.FromSeconds(-6))));

        dto.JobImport.State.ShouldBe(StreamImportState.Unknown);
    }

    // ── Cross-row ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_OneUnknownRowDoesNotChangeTheOthers()
    {
        var dto = Evaluate(Snapshot(
            worker: SourceSample.Missing<WorkerObservation>(),
            store: SourceSample.Of(Now.AddSeconds(-5), false),
            import: Import(TimeSpan.FromMinutes(40), errors: 2)));

        dto.BackgroundJobs.State.ShouldBe(WorkerProcessState.Unknown);
        dto.VolatileStore.State.ShouldBe(VolatileStoreState.Unreachable);
        dto.JobImport.State.ShouldBe(StreamImportState.Overdue);
    }

    [Fact]
    public void Evaluate_TheLimitsAreTiedToTheJobsOwnCadence()
    {
        // The stream job runs on "*/10 * * * *" (pinned against the registrar in the Worker tests): ten minutes.
        const int cadenceMinutes = 10;

        ServiceObservationLimits.ImportOverdueAfter.ShouldBe(TimeSpan.FromMinutes((2 * cadenceMinutes) + 5));
        ServiceObservationLimits.ImportWindow.ShouldBe(TimeSpan.FromHours(24));
    }

    [Fact]
    public void Evaluate_TheAliveWindowHoldsAHeartbeatAndTheLongestAcceptedSamplingInterval_Twice()
    {
        // Hangfire's default heartbeat is 30 s (pinned in the Api tests). The window is judged at read time, when the
        // newest heartbeat is up to one heartbeat interval plus one sampling interval old, so it must hold both, doubled,
        // for the longest interval the options accept. The ceiling is read from the attribute, not from the default.
        const int hangfireHeartbeatSeconds = 30;
        var ceiling = (int)typeof(ServiceObservationOptions)
            .GetProperty(nameof(ServiceObservationOptions.SampleIntervalSeconds))!
            .GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.RangeAttribute), inherit: false)
            .Cast<System.ComponentModel.DataAnnotations.RangeAttribute>()
            .Single()
            .Maximum;

        ServiceObservationLimits.WorkerAliveWithin.ShouldBeGreaterThanOrEqualTo(
            TimeSpan.FromSeconds(2 * (hangfireHeartbeatSeconds + ceiling)));
    }
}
