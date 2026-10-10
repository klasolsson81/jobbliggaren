using Jobbliggaren.Application.Admin.HostObservations;
using Jobbliggaren.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.HostObservations;

/// <summary>
/// The sampler's state machine (#1982). The probe is scripted, so these tests say what the sampler makes of a
/// reading; what the real adapter makes of real files is <c>ProcFsHostObservationProbeTests</c>.
///
/// <para>
/// Which states a real host can produce, per AGENTS.md §5 (Tests): a counter that goes backwards, and an
/// <c>available</c> above <c>total</c>, are states <b>no documented kernel behaviour produces</b> for the
/// grouped sums; those tests assert only that the sampler degrades safely (no number, the right state,
/// recovery) and do not claim what a host does. Reachable states name their actor: the monotonic clock
/// (windows), the sampler stopping (stale), the probe failing to read (Failed), and the first tick after the
/// API process starts (Collecting).
/// </para>
/// </summary>
public class HostObservationSamplerTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly MutableClock _clock = new(Start);
    private readonly ScriptedHostProbe _probe = new();
    private readonly RecordingLogger<HostObservationSampler> _log = new();

    private HostObservationSampler Sampler() =>
        new(_probe, _clock, Options.Create(new HostObservationOptions()), _log);

    /// <summary>Moves both clocks on by <paramref name="seconds"/> and makes the CPU counters rise by the given ticks.</summary>
    private void Elapse(int seconds, long busy, long idle)
    {
        var elapsed = TimeSpan.FromSeconds(seconds);
        _clock.Advance(elapsed);
        _probe.Cpu = ScriptedHostProbe.After(_probe.Cpu.Value, busy, idle, elapsed);
    }

    [Fact]
    public void Defaults_MatchTheDocumentedCadence()
    {
        var options = new HostObservationOptions();

        options.SampleIntervalSeconds.ShouldBe(30);
        options.StaleAfterSeconds.ShouldBe(120);
        options.Validate(new(options)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(30, 90)]
    [InlineData(60, 120)]
    public void Options_ShouldRefuseAStaleLimitNotAboveThreeIntervals(int interval, int stale)
    {
        var results = new HostObservationOptions { SampleIntervalSeconds = interval, StaleAfterSeconds = stale }
            .Validate(new(new object()));

        results.ShouldHaveSingleItem().MemberNames.ShouldBe([nameof(HostObservationOptions.StaleAfterSeconds)]);
    }

    [Fact]
    public void Current_BeforeTheFirstTick_ReportsEverythingAsCollecting()
    {
        var snapshot = Sampler().Current;

        snapshot.Cpu.State.ShouldBe(HostMetricState.Collecting);
        snapshot.Memory.State.ShouldBe(HostMetricState.Collecting);
        snapshot.Disk.State.ShouldBe(HostMetricState.Collecting);
        snapshot.TickAt.ShouldBe(Start);
    }

    [Fact]
    public void Sample_FirstTick_GivesMemoryAndDiskAtOnceAndCpuAsCollecting()
    {
        var sampler = Sampler();

        sampler.Sample();

        var snapshot = sampler.Current;
        snapshot.Cpu.ShouldBe(HostMetric.Collecting<CpuValue>());
        snapshot.Memory.State.ShouldBe(HostMetricState.Available);
        snapshot.Memory.SampledAt.ShouldBe(Start);
        snapshot.Memory.Value.ShouldBe(new MemoryValue(29.4, 2_449_854_464, 8_331_255_808));
        snapshot.Disk.State.ShouldBe(HostMetricState.Available);
    }

    [Fact]
    public void Sample_SecondTick_ComputesCpuOverTheWindowBetweenTheTwoSamples()
    {
        var sampler = Sampler();
        sampler.Sample();
        Elapse(30, busy: 600, idle: 11_400); // 12 000 ticks in 30 s on 4 CPUs at 100 Hz

        sampler.Sample();

        var cpu = sampler.Current.Cpu;
        cpu.State.ShouldBe(HostMetricState.Available);
        cpu.SampledAt.ShouldBe(Start + Interval);
        cpu.Value.ShouldBe(new CpuValue(5.0, 30));
    }

    [Fact]
    public void Sample_AGenuineZero_IsAValueAndNotAnUnknown()
    {
        var sampler = Sampler();
        sampler.Sample();
        Elapse(30, busy: 0, idle: 12_000);

        sampler.Sample();

        sampler.Current.Cpu.State.ShouldBe(HostMetricState.Available);
        sampler.Current.Cpu.Value.ShouldBe(new CpuValue(0.0, 30));
    }

    [Fact]
    public void Sample_AFullyBusyWindow_IsOneHundredPercent()
    {
        var sampler = Sampler();
        sampler.Sample();
        Elapse(30, busy: 12_000, idle: 0);

        sampler.Sample();

        sampler.Current.Cpu.Value.ShouldBe(new CpuValue(100.0, 30));
    }

    [Fact]
    public void Sample_TheWallClockSteppingDuringTheWindow_DoesNotBendTheMeasuredWindow()
    {
        // NTP stepped the wall clock by an hour between two samples; the monotonic window is still 30 s.
        var sampler = Sampler();
        sampler.Sample();
        Elapse(30, busy: 600, idle: 11_400);
        _clock.Advance(TimeSpan.FromHours(1));

        sampler.Sample();

        sampler.Current.Cpu.Value.ShouldBe(new CpuValue(5.0, 30));
    }

    // Declared unreachable for the grouped sums (AGENTS.md §5): the kernel's busy and idle+iowait sums do not
    // decrease while the host is up. The test asserts the degradation only: no number, the new sample is the baseline.
    [Theory]
    [InlineData(-1, 12_000)]
    [InlineData(12_000, -1)]
    public void Sample_AGroupedCounterGoingBackwards_DegradesToFailedAndRebaselines(long busyDelta, long idleDelta)
    {
        var sampler = Sampler();
        sampler.Sample();
        Elapse(30, busyDelta, idleDelta);

        sampler.Sample();

        sampler.Current.Cpu.ShouldBe(HostMetric.Without<CpuValue>(HostMetricReason.CounterReset));
        sampler.Current.Cpu.Value.ShouldBeNull();

        Elapse(30, busy: 1_200, idle: 10_800);
        sampler.Sample();
        sampler.Current.Cpu.Value.ShouldBe(new CpuValue(10.0, 30));
    }

    // Reachable, and the kernel documents it (proc(5)): iowait is not guaranteed monotonic. The adapter folds it
    // into Idle, so what arrives here is a smaller rise of Idle, not a fall.
    [Fact]
    public void Sample_AnIdleRiseSmallerThanUsual_IsAnOrdinaryWindow()
    {
        var sampler = Sampler();
        sampler.Sample();
        Elapse(30, busy: 300, idle: 11_700 - 40);

        sampler.Sample();

        sampler.Current.Cpu.State.ShouldBe(HostMetricState.Available);
    }

    [Fact]
    public void Sample_CountersThatDidNotAdvanceOverAFullWindow_IsAnInvalidIntervalNotAZero()
    {
        var sampler = Sampler();
        sampler.Sample();
        Elapse(30, busy: 0, idle: 0);

        sampler.Sample();

        sampler.Current.Cpu.ShouldBe(HostMetric.Without<CpuValue>(HostMetricReason.IntervalInvalid));
    }

    [Fact]
    public void Sample_AWindowOverThreeIntervals_IsFailedAndTheSampleBecomesTheBaseline()
    {
        // The actor: the sampler did not run (a stalled thread pool, a paused container).
        var sampler = Sampler();
        sampler.Sample();
        Elapse(91, busy: 600, idle: 11_400);

        sampler.Sample();

        sampler.Current.Cpu.ShouldBe(HostMetric.Without<CpuValue>(HostMetricReason.IntervalInvalid));
        Elapse(30, busy: 600, idle: 11_400);
        sampler.Sample();
        sampler.Current.Cpu.Value.ShouldBe(new CpuValue(5.0, 30));
    }

    [Fact]
    public void Sample_AWindowUnderHalfAnInterval_IsSkippedKeepingTheBaselineAndThePreviousReading()
    {
        var sampler = Sampler();
        sampler.Sample();
        Elapse(30, busy: 600, idle: 11_400);
        sampler.Sample();
        var kept = sampler.Current.Cpu;
        Elapse(14, busy: 2_000, idle: 0); // an early extra tick: 14 s, and a burst that must not replace the reading

        sampler.Sample();

        sampler.Current.Cpu.ShouldBe(kept);
        sampler.Current.Cpu.SampledAt.ShouldBe(Start + Interval, "the kept reading keeps its own sample time");

        // The baseline stayed at the 30 s sample, so the next window runs from there: 44 s, 2 600 busy of 14 000.
        Elapse(30, busy: 600, idle: 11_400);
        sampler.Sample();
        sampler.Current.Cpu.Value.ShouldBe(new CpuValue(18.6, 44));
    }

    [Fact]
    public void Sample_AShortWindowBeforeAnyReading_StaysCollecting()
    {
        var sampler = Sampler();
        sampler.Sample();
        Elapse(5, busy: 10, idle: 490);

        sampler.Sample();

        sampler.Current.Cpu.State.ShouldBe(HostMetricState.Collecting);
    }

    [Theory]
    [InlineData(15)]
    [InlineData(90)]
    public void Sample_AWindowAtTheEdgeOfTheAcceptedRange_IsMeasured(int seconds)
    {
        var sampler = Sampler();
        sampler.Sample();
        Elapse(seconds, busy: 100, idle: 900);

        sampler.Sample();

        sampler.Current.Cpu.Value.ShouldBe(new CpuValue(10.0, seconds));
    }

    [Fact]
    public void Sample_AFailedRead_IsReportedAsFailedAndTheOldValueIsNotRepeated()
    {
        var sampler = Sampler();
        sampler.Sample();
        _clock.Advance(Interval);
        _probe.Memory = ProbeResult.Fail<MemoryReading>(HostMetricReason.ReadFailed);

        sampler.Sample();

        var memory = sampler.Current.Memory;
        memory.ShouldBe(HostMetric.Without<MemoryValue>(HostMetricReason.ReadFailed));
        memory.State.ShouldBe(HostMetricState.Failed);
        memory.SampledAt.ShouldBeNull();
        memory.Value.ShouldBeNull();
        sampler.Current.Disk.State.ShouldBe(HostMetricState.Available);
    }

    [Fact]
    public void Sample_AFailedCpuRead_DropsTheBaselineSoTheNextSuccessIsCollecting()
    {
        var sampler = Sampler();
        sampler.Sample();
        _clock.Advance(Interval);
        _probe.Cpu = ProbeResult.Fail<CpuCounters>(HostMetricReason.ParseFailed);
        sampler.Sample();
        sampler.Current.Cpu.ShouldBe(HostMetric.Without<CpuValue>(HostMetricReason.ParseFailed));

        _clock.Advance(Interval);
        _probe.Cpu = ProbeResult.Ok(new CpuCounters(5_000, 50_000, TimeSpan.FromSeconds(4_060)));
        sampler.Sample();

        sampler.Current.Cpu.State.ShouldBe(HostMetricState.Collecting);
    }

    [Fact]
    public void Sample_APlatformWithoutProcfs_IsNotObservableOnEveryReadingAndLogsNothing()
    {
        _probe.Cpu = ProbeResult.Fail<CpuCounters>(HostMetricReason.PlatformUnsupported);
        _probe.Memory = ProbeResult.Fail<MemoryReading>(HostMetricReason.PlatformUnsupported);
        _probe.Disk = ProbeResult.Fail<DiskReading>(HostMetricReason.PlatformUnsupported);
        var sampler = Sampler();

        sampler.Sample();
        sampler.Sample();

        sampler.Current.Cpu.ShouldBe(HostMetric.Without<CpuValue>(HostMetricReason.PlatformUnsupported));
        sampler.Current.Cpu.State.ShouldBe(HostMetricState.NotObservable);
        sampler.Current.Memory.State.ShouldBe(HostMetricState.NotObservable);
        sampler.Current.Disk.State.ShouldBe(HostMetricState.NotObservable);
        _log.Records.ShouldBeEmpty();
    }

    // Declared unreachable (AGENTS.md §5): /proc/meminfo has MemAvailable <= MemTotal. Asserts safe degradation only.
    [Theory]
    [InlineData(0, 100)]
    [InlineData(-1, 100)]
    [InlineData(100, 101)]
    public void Sample_AnImplausibleMemoryReading_DegradesToFailedNeverAPercentage(long total, long available)
    {
        _probe.Memory = ProbeResult.Ok(new MemoryReading(total, available));
        var sampler = Sampler();

        sampler.Sample();

        sampler.Current.Memory.ShouldBe(HostMetric.Without<MemoryValue>(HostMetricReason.Implausible));
    }

    // Declared unreachable (AGENTS.md §5): statfs keeps avail <= free <= size. Asserts safe degradation only.
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(100, 60, 50)] // available above free
    [InlineData(100, 10, 101)] // free above the whole
    public void Sample_AnImplausibleDiskReading_DegradesToFailed(long size, long free, long totalFree)
    {
        _probe.Disk = ProbeResult.Ok(new DiskReading(size, free, totalFree));
        var sampler = Sampler();

        sampler.Sample();

        sampler.Current.Disk.ShouldBe(HostMetric.Without<DiskValue>(HostMetricReason.Implausible));
    }

    [Fact]
    public void Sample_DiskUsesDfArithmeticAndTheTotalIsUsedPlusFree()
    {
        // The production box on 2026-10-10: df printed Size 269 205 880 832, Used 15 866 851 328, Avail
        // 242 287 181 824 and Use% 7 (df rounds up; 6.1 is the same fraction). The ~11 GB between Size and
        // used + avail is the blocks the filesystem reserves for root: in neither.
        var sampler = Sampler();

        sampler.Sample();

        sampler.Current.Disk.Value.ShouldBe(new DiskValue(6.1, 242_287_181_824, 258_154_033_152));
    }

    [Fact]
    public void Sample_MemoryUsesMemAvailableAndNotWhatIsFree()
    {
        var sampler = Sampler();

        sampler.Sample();

        var memory = sampler.Current.Memory.Value.ShouldNotBeNull();
        memory.UsedBytes.ShouldBe((8_135_992L - 5_743_556L) * 1024);
        memory.TotalBytes.ShouldBe(8_135_992L * 1024);
    }

    [Fact]
    public void Sample_AContainerScopedProc_WithholdsCpuAndMemoryButNotDisk()
    {
        _probe.View = ProbeResult.Ok(HostView.ContainerScoped);
        var sampler = Sampler();

        sampler.Sample();

        var snapshot = sampler.Current;
        snapshot.Cpu.ShouldBe(HostMetric.Without<CpuValue>(HostMetricReason.HostViewUnverified));
        snapshot.Memory.ShouldBe(HostMetric.Without<MemoryValue>(HostMetricReason.HostViewUnverified));
        snapshot.Cpu.State.ShouldBe(HostMetricState.NotObservable);
        snapshot.Disk.State.ShouldBe(HostMetricState.Available);
        _log.Records.Count(r => r.EventId.Id == 6222).ShouldBe(2);
    }

    [Fact]
    public void Sample_AContainerScopedProcThatPersists_IsLoggedOnceNotEveryTick()
    {
        _probe.View = ProbeResult.Ok(HostView.ContainerScoped);
        var sampler = Sampler();

        sampler.Sample();
        sampler.Sample();
        sampler.Sample();

        _log.Records.Count(r => r.EventId.Id == 6222).ShouldBe(2, "one line for cpu and one for memory, on the edge only");
    }

    [Fact]
    public void Sample_AViewThatCannotBeRead_CannotDisproveHostScopeAndSamplingProceeds()
    {
        _probe.View = ProbeResult.Fail<HostView>(HostMetricReason.ReadFailed);
        var sampler = Sampler();

        sampler.Sample();

        sampler.Current.Memory.State.ShouldBe(HostMetricState.Available);
    }

    [Fact]
    public void Sample_StampsTheWallClockOnceAfterTheReads()
    {
        // The reads take time; every reading of the tick is attributed to the instant after the last of them.
        _probe.OnRead = () => _clock.Advance(TimeSpan.FromMilliseconds(250));
        var sampler = Sampler();

        sampler.Sample();

        var expected = Start + TimeSpan.FromMilliseconds(1000);
        sampler.Current.TickAt.ShouldBe(expected);
        sampler.Current.Memory.SampledAt.ShouldBe(expected);
        sampler.Current.Disk.SampledAt.ShouldBe(expected);
    }

    [Fact]
    public void Sample_AProbeThatThrows_NeverEscapesAndEveryReadingIsFailed()
    {
        _probe.ThrowOnNextRead = new InvalidOperationException("boom: /proc/stat");
        var sampler = Sampler();

        Should.NotThrow(() => sampler.Sample());

        var snapshot = sampler.Current;
        snapshot.Cpu.ShouldBe(HostMetric.Without<CpuValue>(HostMetricReason.ReadFailed));
        snapshot.Memory.ShouldBe(HostMetric.Without<MemoryValue>(HostMetricReason.ReadFailed));
        snapshot.Disk.ShouldBe(HostMetric.Without<DiskValue>(HostMetricReason.ReadFailed));
        var line = _log.Records.Single(r => r.EventId.Id == 6223);
        line.Level.ShouldBe(LogLevel.Warning);
        line.Message.ShouldNotContain("/proc", Case.Sensitive, "the exception text is not in the message");

        // And the next tick recovers.
        sampler.Sample();
        sampler.Current.Memory.State.ShouldBe(HostMetricState.Available);
    }

    [Fact]
    public void Sample_AStateThatPersists_IsLoggedOnItsEdgesAndNotEveryTick()
    {
        _probe.Disk = ProbeResult.Fail<DiskReading>(HostMetricReason.ReadFailed);
        var sampler = Sampler();

        sampler.Sample();
        sampler.Sample();
        sampler.Sample();
        _probe.Disk = ProbeResult.Ok(new DiskReading(269_205_880_832, 242_287_181_824, 253_339_029_504));
        sampler.Sample();
        sampler.Sample();

        _log.Records.Count(r => r.EventId.Id == 6220).ShouldBe(1, "one warning on the way into failure");
        _log.Records.Count(r => r.EventId.Id == 6221).ShouldBe(1, "one line on the way out");
    }

    [Fact]
    public void Log_NeverCarriesAPathOrAValue()
    {
        _probe.Memory = ProbeResult.Fail<MemoryReading>(HostMetricReason.ParseFailed);
        var sampler = Sampler();

        sampler.Sample();

        var line = _log.Records.Single().Message;
        line.ShouldNotContain("/proc");
        line.ShouldContain("memory");
        line.ShouldContain("ParseFailed");
    }
}
