using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Application.Admin.ServiceObservations;

/// <summary>
/// Reads the three service sources once per <see cref="SampleAsync"/> call and keeps the newest outcome of each as
/// an immutable snapshot (<see cref="IServiceObservationReader"/>). The admin query judges the snapshot against
/// the API's clock when it is read; nothing is judged here.
///
/// <para>
/// <b>Lifetime: singleton.</b> Registered by <c>ServiceObservationRegistration</c> in the Api and called from
/// <c>ServiceObservationService</c>. Every port it takes is singleton-safe (the SQL adapter opens its own scope
/// for the one call it makes), so it survives <c>ValidateScopes</c>. It is not a Mediator message: a synthetic
/// one per tick would put rows in the per-handler latency dataset.
/// </para>
///
/// <para>
/// <b>Honesty.</b> A source that throws or exceeds its timeout becomes <see cref="SourceSample.Missing{T}"/>
/// for that tick, and only that row: the others are read all the same. The previous value is never carried
/// forward as new. The one thing carried across ticks is the newest heartbeat this process has seen, and only
/// as evidence of life with its own time: a Worker that stopped gracefully removes its record, and the last
/// sign of life it gave remains true. Only the exception's type name reaches a log line: a driver message can
/// embed a connection string. Definitions: <c>docs/runbooks/admin-service-observations.md</c>.
/// </para>
/// </summary>
public sealed partial class ServiceObservationSampler(
    IWorkerHeartbeatSource worker,
    IVolatileStoreProbe volatileStore,
    IImportRunRecords imports,
    IDateTimeProvider clock,
    IOptions<ServiceObservationOptions> options,
    ILogger<ServiceObservationSampler> logger) : IServiceObservationReader, IServiceObservationSampler
{
    private const string WorkerSource = "worker-heartbeat";
    private const string VolatileStoreSource = "volatile-store";
    private const string ImportSource = "import-run";

    private ServiceObservationSnapshot _current = ServiceObservationSnapshot.NotSampled(clock.UtcNow);
    private DateTimeOffset? _newestHeartbeat;

    public ServiceObservationSnapshot Current => Volatile.Read(ref _current);

    /// <summary>Takes one sample of every source and publishes the result.</summary>
    public async Task SampleAsync(CancellationToken cancellationToken)
    {
        var observedSince = _current.ObservedSince;

        var workerSample = TakeWorker();
        var storeSample = await TakeAsync(
            VolatileStoreSource, volatileStore.IsReachableAsync, cancellationToken);
        var importSample = await TakeAsync(
            ImportSource,
            async token =>
            {
                var notBefore = clock.UtcNow - ServiceObservationLimits.ImportWindow;
                return new ImportObservation(await imports.LatestStreamRunAsync(JobSource.Platsbanken, notBefore, token));
            },
            cancellationToken);

        Volatile.Write(ref _current, new ServiceObservationSnapshot(observedSince, workerSample, storeSample, importSample));
    }

    private SourceSample<WorkerObservation> TakeWorker()
    {
        try
        {
            var reading = worker.Read();
            var at = clock.UtcNow;

            // Evidence from earlier ticks is kept so that a record the Worker removed on a graceful stop does not
            // erase the last sign of life it gave. A heartbeat from the future is never kept: it is not evidence
            // (the evaluator refuses it), and keeping it would outvote every real one.
            if (reading.NewestHeartbeat is { } seen && seen <= at + ServiceObservationLimits.FutureTolerance)
            {
                _newestHeartbeat = _newestHeartbeat is { } kept && kept > seen ? kept : seen;
            }

            var newest = reading.NewestHeartbeat is { } raw && raw > at + ServiceObservationLimits.FutureTolerance
                ? raw
                : _newestHeartbeat;
            return SourceSample.Of(at, new WorkerObservation(reading.HasRecords, newest));
        }
        catch (Exception ex)
        {
            LogSourceFailed(logger, WorkerSource, ex.GetType().Name);
            return SourceSample.Missing<WorkerObservation>();
        }
    }

    private async Task<SourceSample<T>> TakeAsync<T>(
        string source, Func<CancellationToken, Task<T>> read, CancellationToken stopping)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.ProbeTimeoutSeconds));
        try
        {
            // WaitAsync is the backstop for a source that ignores its token: the tick must not wait for it.
            var value = await read(timeout.Token).WaitAsync(timeout.Token);
            return SourceSample.Of(clock.UtcNow, value);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            LogSourceTimedOut(logger, source);
            return SourceSample.Missing<T>();
        }
        catch (Exception ex)
        {
            LogSourceFailed(logger, source, ex.GetType().Name);
            return SourceSample.Missing<T>();
        }
    }

    [LoggerMessage(EventId = 6230, Level = LogLevel.Warning,
        Message = "ServiceObservationSampler: source {Source} failed ({ExceptionType}) — its row reads unknown, the others are unaffected.")]
    private static partial void LogSourceFailed(ILogger logger, string source, string exceptionType);

    [LoggerMessage(EventId = 6231, Level = LogLevel.Warning,
        Message = "ServiceObservationSampler: source {Source} exceeded its timeout — its row reads unknown, the others are unaffected.")]
    private static partial void LogSourceTimedOut(ILogger logger, string source);
}
