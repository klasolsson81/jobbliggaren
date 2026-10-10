namespace Jobbliggaren.Application.Admin.HostObservations;

/// <summary>CPU use over the window ending at the sample, in percent of all CPUs together.</summary>
public sealed record CpuValue(double Percent, int WindowSeconds);

/// <summary><c>UsedBytes</c> is <c>MemTotal − MemAvailable</c>: reclaimable cache is not counted as used.</summary>
public sealed record MemoryValue(double Percent, long UsedBytes, long TotalBytes);

/// <summary>
/// Disk as <c>df</c> counts it. <c>TotalBytes</c> is used plus free, the space an unprivileged process can
/// ever hold: it leaves out the blocks the filesystem reserves for root, which <c>df</c>'s Size includes.
/// That makes <c>Percent</c> exactly the share of <c>TotalBytes</c> that is not <c>FreeBytes</c>.
/// </summary>
public sealed record DiskValue(double Percent, long FreeBytes, long TotalBytes);

/// <summary>
/// One reading as the sampler holds it. The invariants, pinned by tests: Available and Stale carry a value
/// and a sample time and no reason; Collecting carries nothing; NotObservable and Failed carry a reason and
/// no value. A number that is not known is null, never zero.
/// </summary>
public sealed record HostMetric<T>(HostMetricState State, DateTimeOffset? SampledAt, HostMetricReason? Reason, T? Value)
    where T : class;

/// <summary>Factories for <see cref="HostMetric{T}"/>, which the type system keeps off the generic type itself.</summary>
public static class HostMetric
{
    public static HostMetric<T> Available<T>(DateTimeOffset sampledAt, T value)
        where T : class =>
        new(HostMetricState.Available, sampledAt, null, value);

    public static HostMetric<T> Collecting<T>()
        where T : class =>
        new(HostMetricState.Collecting, null, null, null);

    public static HostMetric<T> Without<T>(HostMetricReason reason)
        where T : class =>
        new(StateFor(reason), null, reason, null);

    private static HostMetricState StateFor(HostMetricReason reason) => reason switch
    {
        HostMetricReason.PlatformUnsupported or HostMetricReason.HostViewUnverified => HostMetricState.NotObservable,
        _ => HostMetricState.Failed,
    };
}

/// <summary>
/// The newest sample, immutable. <c>TickAt</c> is when the tick finished: it dates a reading that has no
/// value of its own (a sampler that has stopped is told apart from one that is merely collecting).
/// </summary>
public sealed record HostObservationSnapshot(
    DateTimeOffset TickAt,
    HostMetric<CpuValue> Cpu,
    HostMetric<MemoryValue> Memory,
    HostMetric<DiskValue> Disk)
{
    /// <summary>Before the first tick has finished: nothing is known, and the clock is already running.</summary>
    public static HostObservationSnapshot NotSampled(DateTimeOffset startedAt) => new(
        startedAt,
        HostMetric.Collecting<CpuValue>(),
        HostMetric.Collecting<MemoryValue>(),
        HostMetric.Collecting<DiskValue>());
}

/// <summary>Where the admin query reads the newest sample. It never samples.</summary>
public interface IHostObservationReader
{
    HostObservationSnapshot Current { get; }
}

/// <summary>
/// One sampling tick. The hosted service in the Api calls it on a timer and never lets a failure out of it;
/// the interface exists so a test can hand that guard a sampler that throws.
/// </summary>
public interface IHostObservationSampler
{
    void Sample();
}
