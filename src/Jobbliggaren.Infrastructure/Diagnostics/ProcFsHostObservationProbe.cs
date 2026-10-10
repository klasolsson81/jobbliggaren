using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Jobbliggaren.Application.Admin.HostObservations;

namespace Jobbliggaren.Infrastructure.Diagnostics;

/// <summary>
/// Reads the host's CPU, memory and disk the way the kernel exposes them (#1982). Runs inside the API
/// container; what it sees is the host's because under runc no namespace virtualises <c>/proc/stat</c> or
/// <c>/proc/meminfo</c> and <c>statfs</c> on the overlay root reports the filesystem beneath it. That is a
/// property of the deployment, so <see cref="ReadHostView"/> looks for the two ways it stops holding.
/// Boundary, definitions and residuals: <c>docs/runbooks/admin-host-observations.md</c>, ADR 0158.
///
/// <para>
/// <b>Paths are constants, never options.</b> A setting can be widened to a file this class should not
/// read. The internal constructor exists only so tests can point at a tree of fixture files. Reads are
/// bounded to 64 KiB, and no file content reaches a log line or an exception message.
/// </para>
///
/// <para>
/// Everything but Linux is <see cref="HostMetricReason.PlatformUnsupported"/>: a developer's Windows machine
/// has no procfs, and a number from some other source would be a different measurement under the same name.
/// </para>
/// </summary>
public sealed class ProcFsHostObservationProbe : IHostObservationProbe
{
    private const string ProcRoot = "/proc";
    private const string CgroupRoot = "/sys/fs/cgroup";
    private const string DiskPath = "/";
    private const string MountInfoPath = "/proc/self/mountinfo";
    private const int MaxFileBytes = 64 * 1024;

    private readonly string _procRoot;
    private readonly string _cgroupRoot;
    private readonly string _mountInfoPath;
    private readonly string _diskPath;
    private readonly bool _isLinux;

    public ProcFsHostObservationProbe()
        : this(ProcRoot, CgroupRoot, MountInfoPath, DiskPath, OperatingSystem.IsLinux())
    {
    }

    internal ProcFsHostObservationProbe(
        string procRoot, string cgroupRoot, string mountInfoPath, string diskPath, bool isLinux)
    {
        (_procRoot, _cgroupRoot, _mountInfoPath, _diskPath, _isLinux) =
            (procRoot, cgroupRoot, mountInfoPath, diskPath, isLinux);
    }

    public ProbeResult<CpuCounters> ReadCpu()
    {
        if (!_isLinux)
        {
            return ProbeResult.Fail<CpuCounters>(HostMetricReason.PlatformUnsupported);
        }

        var text = ReadBounded(Path.Combine(_procRoot, "stat"));
        // The monotonic stamp is taken the moment the counters were read, before they are parsed.
        var at = Stopwatch.GetElapsedTime(0);
        if (!text.IsOk)
        {
            return ProbeResult.Fail<CpuCounters>(text.Failure!.Value);
        }

        return ProcStatParser.TryParse(text.Value, out var busy, out var idle)
            ? ProbeResult.Ok(new CpuCounters(busy, idle, at))
            : ProbeResult.Fail<CpuCounters>(HostMetricReason.ParseFailed);
    }

    public ProbeResult<MemoryReading> ReadMemory()
    {
        if (!_isLinux)
        {
            return ProbeResult.Fail<MemoryReading>(HostMetricReason.PlatformUnsupported);
        }

        var text = ReadBounded(Path.Combine(_procRoot, "meminfo"));
        if (!text.IsOk)
        {
            return ProbeResult.Fail<MemoryReading>(text.Failure!.Value);
        }

        return ProcMeminfoParser.TryParse(text.Value, out var total, out var available)
            ? ProbeResult.Ok(new MemoryReading(total, available))
            : ProbeResult.Fail<MemoryReading>(HostMetricReason.ParseFailed);
    }

    public ProbeResult<DiskReading> ReadDisk()
    {
        if (!_isLinux)
        {
            return ProbeResult.Fail<DiskReading>(HostMetricReason.PlatformUnsupported);
        }

        try
        {
            // statfs of the container's root: the filesystem beneath the overlay, which is where the runtime's
            // storage lives. DriveInfo is a plain statfs on Linux and reads no mount table.
            var drive = new DriveInfo(_diskPath);
            return ProbeResult.Ok(
                new DiskReading(drive.TotalSize, drive.AvailableFreeSpace, drive.TotalFreeSpace));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return ProbeResult.Fail<DiskReading>(HostMetricReason.ReadFailed);
        }
    }

    public ProbeResult<HostView> ReadHostView()
    {
        if (!_isLinux)
        {
            return ProbeResult.Fail<HostView>(HostMetricReason.PlatformUnsupported);
        }

        var checkedAnything = false;

        // Structural: a mount other than the kernel's procfs behind the two files (lxcfs and its kin).
        var mountInfo = ReadBounded(_mountInfoPath);
        if (mountInfo.IsOk)
        {
            checkedAnything = true;
            if (ProcMountInfo.IsContainerScoped(mountInfo.Value))
            {
                return ProbeResult.Ok(HostView.ContainerScoped);
            }
        }

        // Heuristic: MemTotal at or below the container's own memory limit means /proc presents the limit as RAM.
        // "max" and an unreadable file cannot disprove host scope, so they are not findings.
        var limit = ReadBounded(Path.Combine(_cgroupRoot, "memory.max"));
        var memory = ReadBounded(Path.Combine(_procRoot, "meminfo"));
        if (limit.IsOk && memory.IsOk)
        {
            checkedAnything = true;
            if (long.TryParse(limit.Value.AsSpan().Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var bytes)
                && ProcMeminfoParser.TryParse(memory.Value, out var total, out _)
                && total <= bytes)
            {
                return ProbeResult.Ok(HostView.ContainerScoped);
            }
        }

        return checkedAnything
            ? ProbeResult.Ok(HostView.HostScoped)
            : ProbeResult.Fail<HostView>(HostMetricReason.ReadFailed);
    }

    // Reads at most MaxFileBytes. procfs files report a length of 0, so the length cannot bound the read.
    private static ProbeResult<string> ReadBounded(string path)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(MaxFileBytes + 1);
        try
        {
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 1, FileOptions.SequentialScan);
            var read = 0;
            int n;
            while (read <= MaxFileBytes && (n = stream.Read(buffer, read, MaxFileBytes + 1 - read)) > 0)
            {
                read += n;
            }

            return read > MaxFileBytes
                ? ProbeResult.Fail<string>(HostMetricReason.Implausible)
                : ProbeResult.Ok(Encoding.UTF8.GetString(buffer, 0, read));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return ProbeResult.Fail<string>(HostMetricReason.ReadFailed);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
