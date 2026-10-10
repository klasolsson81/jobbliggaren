using Jobbliggaren.Application.Admin.HostObservations;
using Jobbliggaren.Infrastructure.Diagnostics;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.HostObservations;

/// <summary>
/// The real adapter against real files: a temp directory holds the captures of <see cref="HostFixtures"/> in
/// the places the probe reads, so the whole path (open, bounded read, parse, verdict) runs. One test at the
/// end reads the machine's own <c>/proc</c> and asserts only shape.
/// </summary>
public sealed class ProcFsHostObservationProbeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "jbl-host-probe-" + Guid.NewGuid().ToString("N"));

    public ProcFsHostObservationProbeTests() => Directory.CreateDirectory(Path.Combine(_root, "proc"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string Proc(string name) => Path.Combine(_root, "proc", name);

    private string Cgroup(string name) => Path.Combine(_root, "cgroup", name);

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private ProcFsHostObservationProbe Probe(bool isLinux = true) =>
        new(Path.Combine(_root, "proc"), Path.Combine(_root, "cgroup"), Proc("mountinfo"), _root, isLinux);

    private void HealthyHost(string? memoryMax = HostFixtures.ContainerMemoryMax)
    {
        Write(Proc("stat"), HostFixtures.ProcStat);
        Write(Proc("meminfo"), HostFixtures.ProcMeminfo);
        Write(Proc("mountinfo"), HostFixtures.ContainerMountInfo);
        if (memoryMax is not null)
        {
            Write(Cgroup("memory.max"), memoryMax);
        }
    }

    [Fact]
    public void ReadCpu_TheBoxCapture_GivesGroupedCountersAndAMonotonicStamp()
    {
        HealthyHost();
        var probe = Probe();

        var first = probe.ReadCpu();
        var second = probe.ReadCpu();

        first.IsOk.ShouldBeTrue();
        (first.Value.Busy, first.Value.Idle).ShouldBe((HostFixtures.ProcStatBusy, HostFixtures.ProcStatIdle));
        second.Value.At.ShouldBeGreaterThanOrEqualTo(first.Value.At, "the stamp is taken from a monotonic clock");
    }

    [Fact]
    public void ReadMemory_TheBoxCapture_GivesBytes()
    {
        HealthyHost();

        var memory = Probe().ReadMemory();

        memory.IsOk.ShouldBeTrue();
        (memory.Value.TotalBytes, memory.Value.AvailableBytes).ShouldBe((8_135_992L * 1024, 6_016_652L * 1024));
    }

    [Fact]
    public void ReadDisk_ReadsTheFilesystemUnderTheConfiguredPath()
    {
        HealthyHost();

        var disk = Probe().ReadDisk();

        disk.IsOk.ShouldBeTrue();
        disk.Value.TotalBytes.ShouldBeGreaterThan(0);
        disk.Value.FreeBytes.ShouldBeInRange(0, disk.Value.TotalFreeBytes);
        disk.Value.TotalFreeBytes.ShouldBeLessThanOrEqualTo(disk.Value.TotalBytes);
    }

    [Fact]
    public void EveryRead_OnAPlatformWithoutProcfs_IsPlatformUnsupported()
    {
        HealthyHost();
        var probe = Probe(isLinux: false);

        probe.ReadCpu().Failure.ShouldBe(HostMetricReason.PlatformUnsupported);
        probe.ReadMemory().Failure.ShouldBe(HostMetricReason.PlatformUnsupported);
        probe.ReadDisk().Failure.ShouldBe(HostMetricReason.PlatformUnsupported);
        probe.ReadHostView().Failure.ShouldBe(HostMetricReason.PlatformUnsupported);
    }

    [Fact]
    public void Read_OfAFileThatIsMissing_IsReadFailed()
    {
        var probe = Probe();

        probe.ReadCpu().Failure.ShouldBe(HostMetricReason.ReadFailed);
        probe.ReadMemory().Failure.ShouldBe(HostMetricReason.ReadFailed);
    }

    [Fact]
    public void Read_OfSomethingThatIsNotAFile_IsReadFailed()
    {
        Directory.CreateDirectory(Proc("stat"));

        Probe().ReadCpu().Failure.ShouldBe(HostMetricReason.ReadFailed);
    }

    [Fact]
    public void Read_OfCorruptContent_IsParseFailed()
    {
        Write(Proc("stat"), "cpu not numbers at all\n");
        Write(Proc("meminfo"), "MemTotal: lots\n");

        var probe = Probe();

        probe.ReadCpu().Failure.ShouldBe(HostMetricReason.ParseFailed);
        probe.ReadMemory().Failure.ShouldBe(HostMetricReason.ParseFailed);
    }

    [Fact]
    public void Read_OfAFileOverTheBound_IsRefusedWithoutBeingParsed()
    {
        // Not a state a real procfs file reaches (they are a few KB); the bound exists so a wrong path cannot
        // make the sampler read a large file. A valid first line must not rescue it.
        Write(Proc("stat"), HostFixtures.ProcStat + new string('x', 70 * 1024));

        Probe().ReadCpu().Failure.ShouldBe(HostMetricReason.Implausible);
    }

    [Fact]
    public void ReadHostView_TheProductionContainer_IsHostScoped()
    {
        HealthyHost(); // memory.max 1 GiB, far below the 8 GiB the host reports

        Probe().ReadHostView().ShouldBe(ProbeResult.Ok(HostView.HostScoped));
    }

    [Fact]
    public void ReadHostView_NoLimitAtAll_CannotDisproveHostScope()
    {
        HealthyHost("max\n");

        Probe().ReadHostView().ShouldBe(ProbeResult.Ok(HostView.HostScoped));
    }

    [Fact]
    public void ReadHostView_ALimitJustBelowMemTotal_IsStillTheHost()
    {
        HealthyHost("8331255807\n");

        Probe().ReadHostView().ShouldBe(ProbeResult.Ok(HostView.HostScoped));
    }

    // Residual r2 of ADR 0158: the heuristic fails safe the other way too. A cap at or above the host's RAM cannot
    // be told from a bigger host, so the meters are withheld rather than risk describing a container.
    [Fact]
    public void ReadHostView_ALimitAboveMemTotal_FailsSafeAsContainerScoped()
    {
        HealthyHost("99999999999999\n");

        Probe().ReadHostView().ShouldBe(ProbeResult.Ok(HostView.ContainerScoped));
    }

    [Fact]
    public void ReadHostView_MemTotalAtTheLimit_IsContainerScoped()
    {
        // An lxcfs-like mount reports the limit as MemTotal, and then MemTotal equals the limit.
        HealthyHost("8331255808\n");

        Probe().ReadHostView().ShouldBe(ProbeResult.Ok(HostView.ContainerScoped));
    }

    [Fact]
    public void ReadHostView_AnLxcfsMountInfo_IsContainerScopedEvenWithoutAnyMemoryLimit()
    {
        HealthyHost("max\n");
        Write(Proc("mountinfo"), HostFixtures.LxcfsMountInfo);

        Probe().ReadHostView().ShouldBe(ProbeResult.Ok(HostView.ContainerScoped));
    }

    [Fact]
    public void ReadHostView_WhenTheMountTableIsMissingTheLimitStillSpeaks()
    {
        HealthyHost("8331255808\n");
        File.Delete(Proc("mountinfo"));

        Probe().ReadHostView().ShouldBe(ProbeResult.Ok(HostView.ContainerScoped));
    }

    [Fact]
    public void ReadHostView_WhenNothingCouldBeChecked_IsReadFailedAndNotAClaim()
    {
        Write(Proc("meminfo"), HostFixtures.ProcMeminfo); // but no mountinfo and no memory.max

        Probe().ReadHostView().Failure.ShouldBe(HostMetricReason.ReadFailed);
    }

    [Fact]
    public void ReadHostView_CorruptLimit_IsNotAFinding()
    {
        HealthyHost("not a number\n");

        Probe().ReadHostView().ShouldBe(ProbeResult.Ok(HostView.HostScoped));
    }

    [Fact]
    public void TheRealMachine_OnLinux_AnswersEveryReadingWithSaneShapes()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "reads the machine's own /proc, which only Linux has");
        var probe = new ProcFsHostObservationProbe();

        var cpu = probe.ReadCpu();
        var memory = probe.ReadMemory();
        var disk = probe.ReadDisk();
        var view = probe.ReadHostView();

        cpu.IsOk.ShouldBeTrue();
        (cpu.Value.Busy + cpu.Value.Idle).ShouldBeGreaterThan(0);
        memory.IsOk.ShouldBeTrue();
        memory.Value.AvailableBytes.ShouldBeInRange(0, memory.Value.TotalBytes);
        disk.IsOk.ShouldBeTrue();
        disk.Value.TotalBytes.ShouldBeGreaterThan(0);
        view.IsOk.ShouldBeTrue("a Linux CI runner has a readable mountinfo");
    }
}
