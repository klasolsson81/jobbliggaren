using System.Text.Json.Serialization;

namespace Jobbliggaren.Application.Admin.ServiceObservations;

/// <summary>
/// What the Worker process has shown of itself (#1982, ADR 0159). Not "running" and not "working": a Hangfire
/// server writes a heartbeat from its own timer, so the heartbeat is evidence of a live process and of nothing
/// the process does.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<WorkerProcessState>))]
public enum WorkerProcessState
{
    /// <summary>The newest sign of life is at most <see cref="ServiceObservationLimits.WorkerAliveWithin"/> old.</summary>
    Alive,

    /// <summary>A server record exists and its newest heartbeat is older than that.</summary>
    Silent,

    /// <summary>No server record, and no sign of life inside the window, after the sampler has watched for as long.</summary>
    NoServer,

    /// <summary>The source could not be read, is too old to judge, or says something that cannot be true on one clock.</summary>
    Unknown,
}

/// <summary>Whether the volatile Redis (sign-in challenges, OAuth state, registration claims) answers its health check.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<VolatileStoreState>))]
public enum VolatileStoreState
{
    Reachable,
    Unreachable,
    Unknown,
}

/// <summary>
/// What the audit log holds of the Platsbanken stream sync (#1982, ADR 0159). A recorded run proves that the
/// stream job ran and was recorded. It does not prove that JobTech answered: a failed request ends the stream
/// quietly and is recorded as an empty, successful run (ADR 0159, residual s1).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<StreamImportState>))]
public enum StreamImportState
{
    /// <summary>The newest recorded run finished within <see cref="ServiceObservationLimits.ImportOverdueAfter"/>.</summary>
    Recorded,

    /// <summary>A run is recorded inside the window and it is older than that.</summary>
    Overdue,

    /// <summary>No run is recorded inside the window: ingestion off, or the job has not run for a day.</summary>
    NoneRecorded,

    Unknown,
}
