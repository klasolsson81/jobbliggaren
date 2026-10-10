using System.Text.Json.Serialization;

namespace Jobbliggaren.Application.Admin.ServiceObservations.Queries.GetServiceObservation;

/// <summary>
/// What the admin overview receives for the services card, and the whole of it: states, instants, a count of
/// failed events and the three limits as numbers. No server name, queue, worker count, health-check text,
/// latency, audit payload, source name, fetched count or reason ever reaches this type; a test pins the
/// serialised key set. <c>ReadAt</c> is the API clock at the request and travels in the observation header,
/// not in the body.
/// </summary>
public sealed record ServiceObservationDto(
    [property: JsonIgnore] DateTimeOffset ReadAt,
    int StaleAfterSeconds,
    int WorkerAliveWithinSeconds,
    int ImportOverdueAfterSeconds,
    WorkerServiceDto BackgroundJobs,
    VolatileStoreServiceDto VolatileStore,
    JobImportServiceDto JobImport);

/// <summary>
/// The Worker row as a closed union: a state with a time carries it, a state without one carries none. Unknown
/// carries no time at all, so a verdict that could not be judged cannot be shown as an old one.
/// </summary>
public sealed record WorkerServiceDto
{
    private WorkerServiceDto(WorkerProcessState state, DateTimeOffset? sampledAt, DateTimeOffset? heartbeatAt)
    {
        State = state;
        SampledAt = sampledAt;
        HeartbeatAt = heartbeatAt;
    }

    public WorkerProcessState State { get; }

    /// <summary>When the sampler looked. Present in every state but Unknown.</summary>
    public DateTimeOffset? SampledAt { get; }

    /// <summary>The newest sign of life. Present in Alive and Silent only.</summary>
    public DateTimeOffset? HeartbeatAt { get; }

    public static WorkerServiceDto Alive(DateTimeOffset sampledAt, DateTimeOffset heartbeatAt) =>
        new(WorkerProcessState.Alive, sampledAt, heartbeatAt);

    public static WorkerServiceDto Silent(DateTimeOffset sampledAt, DateTimeOffset heartbeatAt) =>
        new(WorkerProcessState.Silent, sampledAt, heartbeatAt);

    public static WorkerServiceDto NoServer(DateTimeOffset sampledAt) =>
        new(WorkerProcessState.NoServer, sampledAt, null);

    public static WorkerServiceDto Unknown { get; } = new(WorkerProcessState.Unknown, null, null);
}

/// <summary>The volatile-store row as a closed union. Only the verdict crosses: the check's own text carries a latency.</summary>
public sealed record VolatileStoreServiceDto
{
    private VolatileStoreServiceDto(VolatileStoreState state, DateTimeOffset? sampledAt)
    {
        State = state;
        SampledAt = sampledAt;
    }

    public VolatileStoreState State { get; }

    /// <summary>When the check was run. Present in Reachable and Unreachable.</summary>
    public DateTimeOffset? SampledAt { get; }

    public static VolatileStoreServiceDto Reachable(DateTimeOffset sampledAt) =>
        new(VolatileStoreState.Reachable, sampledAt);

    public static VolatileStoreServiceDto Unreachable(DateTimeOffset sampledAt) =>
        new(VolatileStoreState.Unreachable, sampledAt);

    public static VolatileStoreServiceDto Unknown { get; } = new(VolatileStoreState.Unknown, null);
}

/// <summary>
/// The import row as a closed union. <c>Errors</c> is the failed events of the recorded run; it is a number and
/// never "0 fel" on the page, because a zero from a run whose request failed reads as a clean bill.
/// </summary>
public sealed record JobImportServiceDto
{
    private JobImportServiceDto(
        StreamImportState state, DateTimeOffset? sampledAt, DateTimeOffset? lastRunAt, int? errors)
    {
        State = state;
        SampledAt = sampledAt;
        LastRunAt = lastRunAt;
        Errors = errors;
    }

    public StreamImportState State { get; }

    /// <summary>When the audit log was read. Present in every state but Unknown.</summary>
    public DateTimeOffset? SampledAt { get; }

    /// <summary>When the newest recorded run finished. Present in Recorded and Overdue.</summary>
    public DateTimeOffset? LastRunAt { get; }

    /// <summary>Failed events of that run. Present in Recorded and Overdue.</summary>
    public int? Errors { get; }

    public static JobImportServiceDto Recorded(DateTimeOffset sampledAt, DateTimeOffset lastRunAt, int errors) =>
        new(StreamImportState.Recorded, sampledAt, lastRunAt, errors);

    public static JobImportServiceDto Overdue(DateTimeOffset sampledAt, DateTimeOffset lastRunAt, int errors) =>
        new(StreamImportState.Overdue, sampledAt, lastRunAt, errors);

    public static JobImportServiceDto NoneRecorded(DateTimeOffset sampledAt) =>
        new(StreamImportState.NoneRecorded, sampledAt, null, null);

    public static JobImportServiceDto Unknown { get; } = new(StreamImportState.Unknown, null, null, null);
}
