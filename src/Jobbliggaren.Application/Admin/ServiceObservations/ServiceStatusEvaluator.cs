using Jobbliggaren.Application.Admin.ServiceObservations.Queries.GetServiceObservation;

namespace Jobbliggaren.Application.Admin.ServiceObservations;

/// <summary>
/// Judges a snapshot against the API's clock at the instant it is read (#1982, ADR 0159). Pure: it reads nothing
/// and takes the clock as an argument, so every verdict is a function of the snapshot and of "now".
///
/// <para>
/// A row whose sample is older than the stale limit is Unknown, never an old verdict: a statement about "now" that
/// is no longer current is not shown with a stale mark, it is withdrawn. A sample or a heartbeat dated later than
/// the API's own clock by more than the future tolerance is Unknown too, because on one clock that cannot be true.
/// No state means that something works. The words are the page's, and they say what was seen and when.
/// </para>
/// </summary>
public static class ServiceStatusEvaluator
{
    public static ServiceObservationDto Evaluate(
        ServiceObservationSnapshot snapshot, DateTimeOffset readAt, ServiceObservationOptions options)
    {
        var staleAfter = TimeSpan.FromSeconds(options.StaleAfterSeconds);

        return new ServiceObservationDto(
            readAt,
            options.StaleAfterSeconds,
            (int)ServiceObservationLimits.WorkerAliveWithin.TotalSeconds,
            (int)ServiceObservationLimits.ImportOverdueAfter.TotalSeconds,
            Worker(snapshot, readAt, staleAfter),
            VolatileStore(snapshot, readAt, staleAfter),
            Import(snapshot, readAt, staleAfter));
    }

    private static WorkerServiceDto Worker(ServiceObservationSnapshot snapshot, DateTimeOffset readAt, TimeSpan staleAfter)
    {
        var sample = snapshot.Worker;
        if (!Judgeable(sample, readAt, staleAfter, out var sampledAt))
        {
            return WorkerServiceDto.Unknown;
        }

        var observation = sample.Value!;
        var alive = ServiceObservationLimits.WorkerAliveWithin;

        if (observation.NewestHeartbeat is not { } heartbeat)
        {
            // A record without a time says nothing. No record and no earlier sign of life is "no server" only once
            // the sampler has watched for as long as the alive window: until then a release in progress reads the same.
            if (observation.HasRecords)
            {
                return WorkerServiceDto.Unknown;
            }

            return readAt - snapshot.ObservedSince >= alive
                ? WorkerServiceDto.NoServer(sampledAt)
                : WorkerServiceDto.Unknown;
        }

        var age = readAt - heartbeat;
        if (age < -ServiceObservationLimits.FutureTolerance)
        {
            return WorkerServiceDto.Unknown;
        }

        if (age <= alive)
        {
            return WorkerServiceDto.Alive(sampledAt, heartbeat);
        }

        return observation.HasRecords
            ? WorkerServiceDto.Silent(sampledAt, heartbeat)
            : WorkerServiceDto.NoServer(sampledAt);
    }

    private static VolatileStoreServiceDto VolatileStore(
        ServiceObservationSnapshot snapshot, DateTimeOffset readAt, TimeSpan staleAfter)
    {
        var sample = snapshot.VolatileStore;
        if (!Judgeable(sample, readAt, staleAfter, out var sampledAt))
        {
            return VolatileStoreServiceDto.Unknown;
        }

        return sample.Value
            ? VolatileStoreServiceDto.Reachable(sampledAt)
            : VolatileStoreServiceDto.Unreachable(sampledAt);
    }

    private static JobImportServiceDto Import(ServiceObservationSnapshot snapshot, DateTimeOffset readAt, TimeSpan staleAfter)
    {
        var sample = snapshot.Import;
        if (!Judgeable(sample, readAt, staleAfter, out var sampledAt))
        {
            return JobImportServiceDto.Unknown;
        }

        if (sample.Value!.Latest is not { } run)
        {
            return JobImportServiceDto.NoneRecorded(sampledAt);
        }

        var age = readAt - run.CompletedAt;
        if (age < -ServiceObservationLimits.FutureTolerance)
        {
            return JobImportServiceDto.Unknown;
        }

        return age > ServiceObservationLimits.ImportOverdueAfter
            ? JobImportServiceDto.Overdue(sampledAt, run.CompletedAt, run.Errors)
            : JobImportServiceDto.Recorded(sampledAt, run.CompletedAt, run.Errors);
    }

    private static bool Judgeable<T>(
        SourceSample<T> sample, DateTimeOffset readAt, TimeSpan staleAfter, out DateTimeOffset sampledAt)
    {
        sampledAt = default;
        if (!sample.HasValue || sample.SampledAt is not { } at)
        {
            return false;
        }

        var age = readAt - at;
        if (age > staleAfter || age < -ServiceObservationLimits.FutureTolerance)
        {
            return false;
        }

        sampledAt = at;
        return true;
    }
}
