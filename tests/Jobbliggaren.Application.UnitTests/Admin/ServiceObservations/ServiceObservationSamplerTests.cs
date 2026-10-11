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
/// A heartbeat dated later than the API's clock: the actor is a backward step of the host's wall clock between the
/// write and the read; the sampler passes it on to be refused and never keeps it as evidence.
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
    public void Current_BeforeTheFirstTick_IsNothingKnown()
    {
        var sampler = Sampler();

        sampler.Current.Worker.HasValue.ShouldBeFalse();
        sampler.Current.VolatileStore.HasValue.ShouldBeFalse();
        sampler.Current.Import.HasValue.ShouldBeFalse();
    }

    [Fact]
    public async Task Sample_TakesAllThreeSources_EachStampedAfterItsOwnRead()
    {
        // The clock moves inside each read: a stamp taken before the read would be earlier than the answer it dates.
        _worker.OnRead = () => _clock.Advance(TimeSpan.FromSeconds(1));
        _store.OnRead = () => _clock.Advance(TimeSpan.FromSeconds(2));
        _imports.OnRead = () => _clock.Advance(TimeSpan.FromSeconds(4));
        _imports.Latest = new StreamImportRun(Start.AddMinutes(-3), Errors: 2);
        var sampler = Sampler();

        await sampler.SampleAsync(Ct);

        var snapshot = sampler.Current;
        snapshot.Worker.SampledAt.ShouldBe(Start.AddSeconds(1));
        snapshot.Worker.Value.ShouldBe(new WorkerObservation(true, Start.AddSeconds(-5), Start.AddSeconds(1)));
        snapshot.VolatileStore.Value.ShouldBeTrue();
        snapshot.VolatileStore.SampledAt.ShouldBe(Start.AddSeconds(3));
        snapshot.Import.SampledAt.ShouldBe(Start.AddSeconds(7));
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

    // ── The Worker source is watched from its first SUCCESSFUL read ─────────────────────────

    [Fact]
    public async Task Sample_WatchedSinceIsTheFirstSuccessfulWorkerRead_AndNoLaterTickMovesIt()
    {
        var sampler = Sampler();
        await sampler.SampleAsync(Ct);
        _clock.Advance(TimeSpan.FromSeconds(30));
        await sampler.SampleAsync(Ct);
        _clock.Advance(TimeSpan.FromSeconds(30));

        await sampler.SampleAsync(Ct);

        sampler.Current.Worker.Value!.WatchedSince.ShouldBe(Start);
    }

    [Fact]
    public async Task Sample_AWorkerSourceThatCouldNotBeRead_HasShownNothing_SoItsWatchingStartsAtTheFirstRead()
    {
        // Postgres starting after the API: two ticks read nothing, the third reads. The 120 s window counts from the third.
        var sampler = Sampler();
        _worker.Throws = new InvalidOperationException("storage down");
        await sampler.SampleAsync(Ct);
        _clock.Advance(TimeSpan.FromSeconds(60));
        await sampler.SampleAsync(Ct);
        _clock.Advance(TimeSpan.FromSeconds(60));
        _worker.Throws = null;

        await sampler.SampleAsync(Ct);

        sampler.Current.Worker.Value!.WatchedSince.ShouldBe(Start.AddSeconds(120));
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

    [Fact]
    public async Task Sample_ShutdownReachesTheSourceThroughTheToken_SoASourceThatHonoursItStopsAtOnce()
    {
        // Without the link the source would hold a token only its own deadline cancels, five seconds later.
        using var shutdown = new CancellationTokenSource();
        _store.Hangs = true;
        var sampler = Sampler();

        var tick = sampler.SampleAsync(shutdown.Token);
        _store.ReceivedToken.IsCancellationRequested.ShouldBeFalse();
        await shutdown.CancelAsync();

        _store.ReceivedToken.IsCancellationRequested.ShouldBeTrue();
        await Should.ThrowAsync<OperationCanceledException>(async () => await tick);
    }

    // ── What reaches a log line ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("worker")]
    [InlineData("store")]
    [InlineData("import")]
    public async Task Sample_AFailureIsLoggedAsItsTypeNameAndNeverItsMessage_ForEverySource(string source)
    {
        // A driver message can embed a connection string. The Worker source fails on every tick of a host without the
        // Hangfire schema (a PostgresException), so it is the one that most needs the pin.
        var secret = new InvalidOperationException("Host=db;Password=hunter2");
        switch (source)
        {
            case "worker":
                _worker.Throws = secret;
                break;
            case "store":
                _store.Throws = secret;
                break;
            default:
                _imports.Throws = secret;
                break;
        }

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

        sampler.Current.Worker.Value.ShouldBe(new WorkerObservation(HasRecords: false, Start.AddSeconds(-20), Start));
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

    // Actor: a backward step of the host's wall clock between the heartbeat's write and this tick. The sampler passes the
    // future time on so that the evaluator refuses it, and never keeps it as evidence: it would outvote every real one.
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
