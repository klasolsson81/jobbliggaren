namespace Jobbliggaren.Application.Admin.HostObservations.Queries.GetHostObservation;

/// <summary>
/// What the admin overview receives for the Server card, and the whole of it: states, instants and numbers.
/// <c>ReadAt</c> is the API clock at the request; each reading's <c>SampledAt</c> is the clock when the host
/// was sampled, so a reading can be old although it was just read. <c>StaleAfterSeconds</c> is the limit the
/// API applied, sent so the page ages a retained reading by the same rule.
/// </summary>
public sealed record HostObservationDto(
    DateTimeOffset ReadAt,
    int StaleAfterSeconds,
    HostMetricDto<CpuValue> Cpu,
    HostMetricDto<MemoryValue> Memory,
    HostMetricDto<DiskValue> Disk);

/// <summary>
/// A reading on the wire. A value and its <c>SampledAt</c> exist together, in Available and Stale only; in
/// every other state both are null. There is no reason, path or text: why a reading is missing stays on
/// the server.
/// </summary>
public sealed record HostMetricDto<T>(HostMetricState State, DateTimeOffset? SampledAt, T? Value)
    where T : class
{
    internal static HostMetricDto<T> From(HostMetric<T> metric) =>
        new(metric.State, metric.SampledAt, metric.Value);
}
