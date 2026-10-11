namespace Jobbliggaren.Application.Admin.ServiceObservations;

/// <summary>
/// One source's newest outcome, immutable. A sample that could not be taken is <see cref="SourceSample.Missing{T}"/>: it has
/// no value and no time, and a later tick never fills it from an earlier one. <c>SampledAt</c> is the clock
/// read once, after the source answered.
/// </summary>
public sealed record SourceSample<T>(bool HasValue, DateTimeOffset? SampledAt, T? Value);

/// <summary>Factories for <see cref="SourceSample{T}"/>, which the type system keeps off the generic type itself.</summary>
public static class SourceSample
{
    public static SourceSample<T> Missing<T>() => new(false, null, default);

    public static SourceSample<T> Of<T>(DateTimeOffset sampledAt, T value) => new(true, sampledAt, value);
}

/// <summary>
/// What the Worker row stands on: whether a record exists now, the newest sign of life this process has seen, and when
/// the Worker source was first READ successfully. "No server" needs a sampler that has read the storage for as long as the alive
/// window, counted from that first successful read and not from the process start: a storage that could not be read
/// has shown nothing, and a release in progress reads the same as a missing Worker until the window has passed.
/// </summary>
public sealed record WorkerObservation(bool HasRecords, DateTimeOffset? NewestHeartbeat, DateTimeOffset WatchedSince);

/// <summary>What the import row stands on: the newest recorded stream run inside the window, or none.</summary>
public sealed record ImportObservation(StreamImportRun? Latest);

/// <summary>The newest tick, immutable.</summary>
public sealed record ServiceObservationSnapshot(
    SourceSample<WorkerObservation> Worker,
    SourceSample<bool> VolatileStore,
    SourceSample<ImportObservation> Import)
{
    /// <summary>Before the first tick has finished: nothing is known.</summary>
    public static ServiceObservationSnapshot NotSampled() => new(
        SourceSample.Missing<WorkerObservation>(),
        SourceSample.Missing<bool>(),
        SourceSample.Missing<ImportObservation>());
}

/// <summary>Where the admin query reads the newest sample. It never samples.</summary>
public interface IServiceObservationReader
{
    ServiceObservationSnapshot Current { get; }
}

/// <summary>
/// One sampling tick. The hosted service in the Api calls it on a timer and never lets a failure out of it; the
/// interface exists so a test can hand that guard a sampler that throws.
/// </summary>
public interface IServiceObservationSampler
{
    Task SampleAsync(CancellationToken cancellationToken);
}
