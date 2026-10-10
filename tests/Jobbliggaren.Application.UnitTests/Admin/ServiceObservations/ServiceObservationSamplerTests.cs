using Jobbliggaren.Application.Admin.ServiceObservations;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.ServiceObservations;

/// <summary>
/// The sampler's state machine (#1982, ADR 0159). The ports are scripted, so these tests say what the sampler makes
/// of an answer; what the real adapters make of real storage is in the Api integration tests.
///
/// <para>
/// Which states a real system can produce, per AGENTS.md §5 (Tests). A source that throws or times out: the driver
/// failing (a Postgres or Redis that is down or slow), which is what the fault tests stand for. The newest heartbeat
/// carried across ticks: a Worker that stopped gracefully removes its own server record (the actor is Hangfire's
/// <c>BackgroundProcessingServer.Dispose</c>, proven against real storage in <c>HangfireWorkerHeartbeatSourceTests</c>).
/// Declared unreachable, so the test asserts only that the sampler degrades safely: a heartbeat dated in the future
/// (the heartbeat and the API share the host's clock).
/// </para>
/// </summary>
public sealed class ServiceObservationSamplerTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly MutableClock _clock = new(Start);
    private readonly ScriptedWorkerSource _worker = new();
    private readonly ScriptedStoreProbe _store = new();
    private readonly ScriptedImportRecords _imports = new();
    private readonly RecordingLogger<ServiceObservationSampler> _log = new();

    public ServiceObservationSamplerTests()
    {
        _worker.Reading = new WorkerHeartbeatReading(HasRecords: true, Start.AddSeconds(-5));
    }

    private ServiceObservationSampler Sampler(int probeTimeoutSeconds = 5) => new(
        _worker, _store, _imports, _clock,
        Options.Create(new ServiceObservationOptions { ProbeTimeoutSeconds = probeTimeoutSeconds }), _log);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Current_BeforeTheFirstTick_IsNothingKnownAndTheClockIsRunning()
    {
        var sampler = Sampler();

        sampler.Current.ObservedSince.ShouldBe(Start);
        sampler.Current.Worker.HasValue.ShouldBeFalse();
        sampler.Current.VolatileStore.HasValue.ShouldBeFalse();
        sampler.Current.Import.HasValue.ShouldBeFalse();
    }

    [Fact]
    public async Task Sample_TakesAllThreeSources_EachStampedAfterItsOwnRead()
    {
        // The clock moves inside each read: a stamp taken before the read would be earlier than the answer it dates.
        _worker.OnRead = () => _clock.Advance(TimeSpan.FromSeconds(1));
        _imports.Latest = new StreamImportRun(Start.AddMinutes(-3), Errors: 2);
        var sampler = Sampler();

        await sampler.SampleAsync(Ct);

        var snapshot = sampler.Current;
        snapshot.Worker.SampledAt.ShouldBe(Start.AddSeconds(1));
        snapshot.Worker.Value.ShouldBe(new WorkerObservation(true, Start.AddSeconds(-5)));
        snapshot.VolatileStore.Value.ShouldBeTrue();
        snapshot.VolatileStore.SampledAt.ShouldBe(Start.AddSeconds(1));
        snapshot.Import.Value.ShouldBe(new ImportObservation(new StreamImportRun(Start.AddMinutes(-3), 2)));
    }

    [Fact]
    public async Task Sample_AsksTheImportRecordsForPlatsbankenInsideTheWindowOfTheCallersClock()
    {
        var sampler = Sampler();

        await sampler.SampleAsync(Ct);

        _imports.AskedSource.ShouldBe(JobSource.Platsbanken);
        _imports.AskedNotBefore.ShouldBe(Start - ServiceObservationLimits.ImportWindow);
    }

    [Fact]
    public async Task Sample_ObservedSinceIsWhenTheSamplerWasBuilt_AndNoTickMovesIt()
    {
        var sampler = Sampler();
        _clock.Advance(TimeSpan.FromMinutes(5));

        await sampler.SampleAsync(Ct);

        sampler.Current.ObservedSince.ShouldBe(Start);
    }

    // ── One source failing never blanks the others ──────────────────────────────────────────

    [Fact]
    public async Task Sample_AWorkerSourceThatThrows_IsMissingAndTheOtherTwoAreStillRead()
    {
        _worker.Throws = new InvalidOperationException("storage down");
        var sampler = Sampler();

        await sampler.SampleAsync(Ct);

        sampler.Current.Worker.HasValue.ShouldBeFalse();
        sampler.Current.VolatileStore.HasValue.ShouldBeTrue();
        sampler.Current.Import.HasValue.ShouldBeTrue();
    }

    [Fact]
    public async Task Sample_AStoreProbeThatThrows_IsMissingAndTheOtherTwoAreStillRead()
    {
        _store.Throws = new InvalidOperationException("no verdict");
        var sampler = Sampler();

        await sampler.SampleAsync(Ct);

        sampler.Current.VolatileStore.HasValue.ShouldBeFalse();
        sampler.Current.Worker.HasValue.ShouldBeTrue();
        sampler.Current.Import.HasValue.ShouldBeTrue();
    }

    [Fact]
    public async Task Sample_AnImportReadThatThrows_IsMissingAndTheOtherTwoAreStillRead()
    {
        _imports.Throws = new TimeoutException("slow");
        var sampler = Sampler();

        await sampler.SampleAsync(Ct);

        sampler.Current.Import.HasValue.ShouldBeFalse();
        sampler.Current.Worker.HasValue.ShouldBeTrue();
        sampler.Current.VolatileStore.HasValue.ShouldBeTrue();
    }

    [Fact]
    public async Task Sample_AFailedTick_DoesNotCarryTheEarlierValueForwardAsNew()
    {
        var sampler = Sampler();
        await sampler.SampleAsync(Ct);
        sampler.Current.VolatileStore.HasValue.ShouldBeTrue();

        _clock.Advance(TimeSpan.FromSeconds(30));
        _store.Throws = new InvalidOperationException("down");
        await sampler.SampleAsync(Ct);

        sampler.Current.VolatileStore.HasValue.ShouldBeFalse();
        sampler.Current.VolatileStore.SampledAt.ShouldBeNull();
    }

    [Fact]
    public async Task Sample_ASourceThatNeverAnswers_IsGivenUpOnAtItsDeadlineAndTheTickStillEnds()
    {
        _store.Hangs = true;
        var sampler = Sampler(probeTimeoutSeconds: 1);

        var tick = sampler.SampleAsync(Ct);
        var finished = await Task.WhenAny(tick, Task.Delay(TimeSpan.FromSeconds(10), Ct));

        finished.ShouldBe(tick, "the tick waited for a source that ignores its token");
        await tick;
        sampler.Current.VolatileStore.HasValue.ShouldBeFalse();
        sampler.Current.Worker.HasValue.ShouldBeTrue();
        _log.Records.ShouldHaveSingleItem().EventId.Id.ShouldBe(6231);
    }

    [Fact]
    public async Task Sample_ShutdownDuringATick_IsPassedOnAndNotRecordedAsAFailedSource()
    {
        using var shutdown = new CancellationTokenSource();
        _store.Hangs = true;
        var sampler = Sampler();

        var tick = sampler.SampleAsync(shutdown.Token);
        await shutdown.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await tick);
        _log.Records.ShouldBeEmpty();
    }

    // ── What reaches a log line ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Sample_AFailureIsLoggedAsItsTypeNameAndNeverItsMessage()
    {
        // A driver message can embed a connection string.
        _imports.Throws = new InvalidOperationException("Host=db;Password=hunter2");
        var sampler = Sampler();

        await sampler.SampleAsync(Ct);

        var line = _log.Records.ShouldHaveSingleItem();
        (line.Level, line.EventId.Id).ShouldBe((LogLevel.Warning, 6230));
        line.Message.ShouldContain("InvalidOperationException");
        line.Message.ShouldNotContain("hunter2");
        line.Message.ShouldNotContain("Password");
    }

    // ── The one thing carried across ticks: the newest sign of life ────────────────────────

    [Fact]
    public async Task Sample_TheNewestHeartbeatSeenSurvivesARecordThatHasGone()
    {
        var sampler = Sampler();
        _worker.Reading = new WorkerHeartbeatReading(HasRecords: true, Start.AddSeconds(-20));
        await sampler.SampleAsync(Ct);

        // The Worker stopped gracefully and removed its record.
        _clock.Advance(TimeSpan.FromSeconds(30));
        _worker.Reading = new WorkerHeartbeatReading(HasRecords: false, NewestHeartbeat: null);
        await sampler.SampleAsync(Ct);

        sampler.Current.Worker.Value.ShouldBe(new WorkerObservation(HasRecords: false, Start.AddSeconds(-20)));
    }

    [Fact]
    public async Task Sample_TheNewestOfTheEvidenceWins_AnOlderHeartbeatNeverReplacesIt()
    {
        var sampler = Sampler();
        _worker.Reading = new WorkerHeartbeatReading(HasRecords: true, Start.AddSeconds(-5));
        await sampler.SampleAsync(Ct);

        _clock.Advance(TimeSpan.FromSeconds(30));
        _worker.Reading = new WorkerHeartbeatReading(HasRecords: true, Start.AddSeconds(-50));
        await sampler.SampleAsync(Ct);

        sampler.Current.Worker.Value!.NewestHeartbeat.ShouldBe(Start.AddSeconds(-5));
    }

    [Fact]
    public async Task Sample_AFailedWorkerReadDoesNotEraseTheCarriedEvidence()
    {
        var sampler = Sampler();
        await sampler.SampleAsync(Ct);

        _clock.Advance(TimeSpan.FromSeconds(30));
        _worker.Throws = new InvalidOperationException("storage down");
        await sampler.SampleAsync(Ct);
        _clock.Advance(TimeSpan.FromSeconds(30));
        _worker.Throws = null;
        _worker.Reading = new WorkerHeartbeatReading(HasRecords: false, NewestHeartbeat: null);
        await sampler.SampleAsync(Ct);

        sampler.Current.Worker.Value!.NewestHeartbeat.ShouldBe(Start.AddSeconds(-5));
    }

    // Unreachable on one clock; asserts only that a future heartbeat reaches the evaluator (which refuses it) and
    // does not outvote the real evidence for the rest of the process.
    [Fact]
    public async Task Sample_AHeartbeatFromTheFuture_IsPassedOnToBeRefusedAndNeverKeptAsEvidence()
    {
        var sampler = Sampler();
        _worker.Reading = new WorkerHeartbeatReading(HasRecords: true, Start.AddHours(1));
        await sampler.SampleAsync(Ct);

        sampler.Current.Worker.Value!.NewestHeartbeat.ShouldBe(Start.AddHours(1));

        _clock.Advance(TimeSpan.FromSeconds(30));
        _worker.Reading = new WorkerHeartbeatReading(HasRecords: true, Start.AddSeconds(-2));
        await sampler.SampleAsync(Ct);

        sampler.Current.Worker.Value!.NewestHeartbeat.ShouldBe(Start.AddSeconds(-2));
    }
}
