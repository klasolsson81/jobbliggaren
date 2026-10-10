using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Application.Admin.HostObservations.Queries.GetHostObservation;

/// <summary>
/// Reads the newest sample from memory and decides, against the API clock, what it may still claim. No I/O:
/// the host is never touched by a request, so the cost of this read does not depend on who asks.
/// </summary>
public sealed class GetHostObservationQueryHandler(
    IHostObservationReader reader,
    IDateTimeProvider clock,
    IOptions<HostObservationOptions> options) : IQueryHandler<GetHostObservationQuery, HostObservationDto>
{
    // A sample dated later than the API's own clock by more than this was stamped by a wall clock that has
    // since stepped backwards: the clock is the actor that produces such a state, and it is not an observation.
    private static readonly TimeSpan FutureTolerance = TimeSpan.FromSeconds(5);

    public ValueTask<HostObservationDto> Handle(GetHostObservationQuery query, CancellationToken cancellationToken)
    {
        var readAt = clock.UtcNow.ToUniversalTime();
        var staleAfter = TimeSpan.FromSeconds(options.Value.StaleAfterSeconds);
        var snapshot = reader.Current;
        var tickAge = readAt - snapshot.TickAt;

        return ValueTask.FromResult(new HostObservationDto(
            readAt,
            options.Value.StaleAfterSeconds,
            HostMetricDto<CpuValue>.From(Age(snapshot.Cpu, readAt, tickAge, staleAfter)),
            HostMetricDto<MemoryValue>.From(Age(snapshot.Memory, readAt, tickAge, staleAfter)),
            HostMetricDto<DiskValue>.From(Age(snapshot.Disk, readAt, tickAge, staleAfter))));
    }

    private static HostMetric<T> Age<T>(HostMetric<T> metric, DateTimeOffset readAt, TimeSpan tickAge, TimeSpan staleAfter)
        where T : class
    {
        switch (metric.State)
        {
            // A fact about the deployment; a stopped sampler changes nothing about it.
            case HostMetricState.NotObservable:
                return metric;

            case HostMetricState.Available when metric.SampledAt is { } sampledAt:
                var age = readAt - sampledAt;
                if (age < -FutureTolerance)
                {
                    return HostMetric.Without<T>(HostMetricReason.Implausible);
                }

                return age > staleAfter ? metric with { State = HostMetricState.Stale } : metric;

            // Nothing to show and nothing sampling: a sampler that never finished a tick, or stopped.
            default:
                return tickAge > staleAfter ? HostMetric.Without<T>(HostMetricReason.SamplerStalled) : metric;
        }
    }
}
