using Jobbliggaren.Infrastructure.Diagnostics;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.HostObservations;

/// <summary>
/// The parsers over what the producers emit (<see cref="HostFixtures"/>: captured on the production box) and
/// over the shapes the kernel documents but the box does not happen to show (proc(5)). Malformed input is
/// declared unreachable from a real kernel (AGENTS.md §5): those cases assert only that the parser says
/// "no" and never invents a number.
/// </summary>
public class ProcParsersTests
{
    [Fact]
    public void ProcStat_TheBoxCapture_GroupsBusyAndIdleTheDocumentedWay()
    {
        ProcStatParser.TryParse(HostFixtures.ProcStat, out var busy, out var idle).ShouldBeTrue();

        // user + nice + system + irq + softirq + steal, and idle + iowait.
        busy.ShouldBe(20_067_891L + 69_831 + 9_369_748 + 0 + 1_674_696 + 4_422);
        busy.ShouldBe(HostFixtures.ProcStatBusy);
        idle.ShouldBe(1_889_627_981L + 820_642);
        idle.ShouldBe(HostFixtures.ProcStatIdle);
    }

    [Fact]
    public void ProcStat_TheGuestColumns_AreNotCountedTwice()
    {
        // proc(5): guest time is already inside user (and guest_nice inside nice).
        ProcStatParser.TryParse("cpu  100 0 0 900 0 0 0 0 7777 8888\n", out var busy, out var idle).ShouldBeTrue();

        (busy, idle).ShouldBe((100L, 900L));
    }

    [Fact]
    public void ProcStat_IsReadFromTheAggregateLineAndNotFromOneCpu()
    {
        ProcStatParser.TryParse("cpu0 1 1 1 1 1 1 1 1 0 0\ncpu1 2 2 2 2 2 2 2 2 0 0\n", out _, out _).ShouldBeFalse();
        ProcStatParser.TryParse("intr 1 2 3\ncpu 10 0 0 90 0 0 0 0 0 0\ncpu0 99 0 0 1 0 0 0 0 0 0\n", out var busy, out _)
            .ShouldBeTrue();
        busy.ShouldBe(10);
    }

    [Theory]
    [InlineData("cpu 10 0 0 90 0 0 0 0\n")] // eight columns: Linux 2.6.11 and later, through steal
    [InlineData("cpu  10 0 0 90 0 0 0 0 0 0\r\n")] // CRLF
    [InlineData("cpu\t10 0 0 90 0 0 0 0 0 0\n")] // a tab after the name
    [InlineData("cpu  10 0 0 90 0 0 0 0 0 0 5 5\n")] // columns a future kernel adds are ignored
    public void ProcStat_AcceptsTheDocumentedShapes(string text)
    {
        ProcStatParser.TryParse(text, out var busy, out var idle).ShouldBeTrue();

        (busy, idle).ShouldBe((10L, 90L));
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage\n")]
    [InlineData("cpu\n")]
    [InlineData("cpu 10 0 0 90 0 0 0\n")] // seven columns: steal missing
    [InlineData("cpu 10 0 x 90 0 0 0 0\n")]
    [InlineData("cpu 10 0 -1 90 0 0 0 0\n")]
    [InlineData("cpu 10 0 0 90 0 0 0 99999999999999999999\n")] // overflows a long
    [InlineData("cpu 9223372036854775807 1 0 0 0 0 0 0\n")] // the sum overflows
    public void ProcStat_RefusesWhatIsNotACounterLine(string text)
    {
        ProcStatParser.TryParse(text, out var busy, out var idle).ShouldBeFalse();

        (busy, idle).ShouldBe((0L, 0L));
    }

    [Fact]
    public void Meminfo_TheBoxCapture_ReadsTotalAndAvailableInBytes()
    {
        ProcMeminfoParser.TryParse(HostFixtures.ProcMeminfo, out var total, out var available).ShouldBeTrue();

        total.ShouldBe(8_135_992L * 1024);
        available.ShouldBe(6_016_652L * 1024, "MemAvailable, not MemFree (1 256 212 kB on the same capture)");
    }

    [Fact]
    public void Meminfo_WithoutMemAvailable_HasNoAnswerAndDoesNotFallBackToMemFree()
    {
        const string before314 = "MemTotal:        8135992 kB\nMemFree:         1256212 kB\nCached:          4468400 kB\n";

        ProcMeminfoParser.TryParse(before314, out _, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("MemTotal: 100 MB\nMemAvailable: 50 kB\n")] // the unit proc(5) documents is kB
    [InlineData("MemTotal: 100\nMemAvailable: 50 kB\n")] // no unit
    [InlineData("MemTotal: x kB\nMemAvailable: 50 kB\n")]
    [InlineData("MemTotal: -100 kB\nMemAvailable: 50 kB\n")]
    [InlineData("MemTotal: 9223372036854775807 kB\nMemAvailable: 50 kB\n")] // times 1024 overflows
    [InlineData("MemTotal: 100 kB\n")]
    [InlineData("")]
    public void Meminfo_RefusesWhatIsNotAKilobyteCount(string text)
    {
        ProcMeminfoParser.TryParse(text, out _, out _).ShouldBeFalse();
    }

    [Fact]
    public void Meminfo_MatchesTheWholeKeyAndTakesTheFirstOccurrence()
    {
        const string text = "MemTotalX: 1 kB\nMemTotal: 100 kB\nMemTotal: 999 kB\nMemAvailableY: 7 kB\nMemAvailable: 40 kB\n";

        ProcMeminfoParser.TryParse(text, out var total, out var available).ShouldBeTrue();

        (total, available).ShouldBe((100L * 1024, 40L * 1024));
    }

    [Fact]
    public void MountInfo_TheProductionContainer_IsNotContainerScoped()
    {
        // Docker/runc binds /proc/bus, /proc/sys and others read-only and masks /proc/kcore, /proc/keys... with
        // /dev/null, but nothing stands behind /proc/stat or /proc/meminfo, and /proc itself is proc.
        ProcMountInfo.IsContainerScoped(HostFixtures.ContainerMountInfo).ShouldBeFalse();
    }

    [Fact]
    public void MountInfo_AnLxcfsMountOverMeminfoAndStat_IsContainerScoped()
    {
        ProcMountInfo.IsContainerScoped(HostFixtures.LxcfsMountInfo).ShouldBeTrue();
    }

    [Theory]
    [InlineData("612 415 0:140 /proc/meminfo /proc/meminfo rw - fuse.lxcfs lxcfs rw")]
    [InlineData("613 415 0:140 /proc/stat /proc/stat rw,nosuid master:3 - fuse.lxcfs lxcfs rw")] // an optional field before the dash
    [InlineData("415 575 0:71 / /proc rw,nosuid - tmpfs tmpfs rw")] // /proc is not procfs
    public void MountInfo_EitherStructuralFinding_IsContainerScoped(string line)
    {
        ProcMountInfo.IsContainerScoped(HostFixtures.ContainerMountInfo + "\n" + line).ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a mountinfo line\n")]
    [InlineData("575 520 0:55 / / rw - overlay overlay rw\n")] // no /proc line says nothing either way
    [InlineData("612 415 0:140 / /proc/my\\040stat rw - fuse.lxcfs lxcfs rw\n")] // an escaped space is another path
    [InlineData("612 415 0:140 /x /proc/cpuinfo rw - fuse.lxcfs lxcfs rw\n")] // only the two files the card reads
    public void MountInfo_OtherwiseThereIsNoFinding(string text)
    {
        ProcMountInfo.IsContainerScoped(text).ShouldBeFalse();
    }
}
