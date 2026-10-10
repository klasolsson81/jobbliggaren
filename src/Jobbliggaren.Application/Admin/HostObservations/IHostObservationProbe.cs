namespace Jobbliggaren.Application.Admin.HostObservations;

/// <summary>
/// The raw facts of one host sample, read from the kernel. Application defines the port, Infrastructure
/// reads <c>/proc</c> and the filesystem (<c>ProcFsHostObservationProbe</c>). The port speaks in cumulative
/// CPU time, bytes and a verdict, never in procfs column names: the grouping lives in the adapter. A probe
/// does not throw: a reading it cannot produce comes back as a <see cref="ProbeResult{T}"/> carrying the reason.
/// </summary>
public interface IHostObservationProbe
{
    ProbeResult<CpuCounters> ReadCpu();

    ProbeResult<MemoryReading> ReadMemory();

    ProbeResult<DiskReading> ReadDisk();

    /// <summary>Whether <c>/proc</c> in this process describes the host or only its container.</summary>
    ProbeResult<HostView> ReadHostView();
}

/// <summary>A reading, or the reason there is none. Built with <see cref="ProbeResult"/>.</summary>
public readonly record struct ProbeResult<T>
{
    internal ProbeResult(T value, HostMetricReason? failure, bool isOk)
    {
        Value = value;
        Failure = failure;
        IsOk = isOk;
    }

    public T Value { get; }

    public HostMetricReason? Failure { get; }

    /// <summary>False for <c>default</c> too, so an uninitialised result never reads as a reading.</summary>
    public bool IsOk { get; }
}

/// <summary>Factories for <see cref="ProbeResult{T}"/>, which the type system keeps off the generic type itself.</summary>
public static class ProbeResult
{
    public static ProbeResult<T> Ok<T>(T value) => new(value, null, isOk: true);

    public static ProbeResult<T> Fail<T>(HostMetricReason reason) => new(default!, reason, isOk: false);
}

/// <summary>
/// Cumulative CPU time of every CPU together, in the kernel's clock ticks. <c>Idle</c> is idle plus iowait;
/// <c>Busy</c> is user, nice, system, irq, softirq and steal (the guest columns are left out: the kernel
/// already counts guest time inside user and nice). <c>At</c> is a monotonic timestamp taken when the
/// counters were read, so the window between two samples survives a step of the wall clock.
/// </summary>
public readonly record struct CpuCounters(long Busy, long Idle, TimeSpan At);

/// <summary><c>MemAvailable</c> is the kernel's estimate of what can be allocated without swapping.</summary>
public readonly record struct MemoryReading(long TotalBytes, long AvailableBytes);

/// <summary>
/// The filesystem as <c>statfs</c> reports it: <c>FreeBytes</c> is what an unprivileged process can still
/// use (blocks available), <c>TotalFreeBytes</c> includes the blocks the filesystem reserves for root.
/// </summary>
public readonly record struct DiskReading(long TotalBytes, long FreeBytes, long TotalFreeBytes);

/// <summary>The adapter's verdict on whether the figures it reads are the host's.</summary>
public enum HostView
{
    /// <summary>Nothing found that makes <c>/proc</c> container-scoped. Not a proof: see the runbook.</summary>
    HostScoped,

    /// <summary>A mount over <c>/proc/stat</c> or <c>/proc/meminfo</c>, a non-proc <c>/proc</c>, or <c>MemTotal</c> at or below the container's memory limit.</summary>
    ContainerScoped,
}
