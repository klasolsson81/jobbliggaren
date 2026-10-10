using Jobbliggaren.Domain.JobAds;

namespace Jobbliggaren.Application.Admin.ServiceObservations;

/// <summary>
/// The Worker's sign of life, from the storage the Hangfire server writes its heartbeat to. The adapter lives in
/// the Api next to the other Hangfire code (Application stays Hangfire-free). Hangfire exposes only a synchronous
/// monitoring API, so this port is synchronous too; the sampler calls it from its own tick, never from a request.
/// The adapter may throw: the sampler turns any failure into an unknown row.
/// </summary>
public interface IWorkerHeartbeatSource
{
    WorkerHeartbeatReading Read();
}

/// <summary>
/// <c>HasRecords</c> is whether any server record exists; <c>NewestHeartbeat</c> is the newest heartbeat among
/// them, null when there are no records or none carries a time. A server count is deliberately not here: after
/// a kill a stale record and a fresh one coexist until a live server's watchdog removes the old one, so a count
/// says nothing about whether the Worker lives.
/// </summary>
public readonly record struct WorkerHeartbeatReading(bool HasRecords, DateTimeOffset? NewestHeartbeat);

/// <summary>
/// Whether the volatile Redis (sign-in challenges, OAuth state, registration claims) answers its registered
/// readiness check. True is Healthy and false is Unhealthy; a check that could not be run at all throws. Only
/// the verdict crosses: the check's own description carries a latency and is never forwarded.
/// </summary>
public interface IVolatileStoreProbe
{
    Task<bool> IsReachableAsync(CancellationToken cancellationToken);
}

/// <summary>
/// What the audit log holds of the job-import runs. Named for the records and not for one question so that the
/// logs page (#1980) can extend it; this slice builds only "the newest stream run".
/// </summary>
public interface IImportRunRecords
{
    /// <summary>
    /// The newest recorded stream run of <paramref name="source"/> that finished at or after
    /// <paramref name="notBefore"/>, or null when there is none. The bound comes from the caller's clock, never from
    /// the database's, and it is what keeps "nothing was ever written" from scanning the whole log.
    /// </summary>
    Task<StreamImportRun?> LatestStreamRunAsync(
        JobSource source, DateTimeOffset notBefore, CancellationToken cancellationToken);
}

/// <summary>A recorded stream run: when it finished and how many of its events failed. Fetched is not here: its zero is ambiguous.</summary>
public sealed record StreamImportRun(DateTimeOffset CompletedAt, int Errors);
