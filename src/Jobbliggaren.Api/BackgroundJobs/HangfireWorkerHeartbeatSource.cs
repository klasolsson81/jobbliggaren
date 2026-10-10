using Hangfire;
using Jobbliggaren.Application.Admin.ServiceObservations;

namespace Jobbliggaren.Api.BackgroundJobs;

/// <summary>
/// The Worker's sign of life, read from the Hangfire storage the Worker's server writes its heartbeat to
/// (#1982, ADR 0159). Api composition-root code beside <see cref="HangfireBackgroundJobController"/>: all Hangfire
/// types stay here, and only a flag and an instant cross the port. Nothing about a server (its name, queues or
/// worker count) is read into the reading.
///
/// <para>
/// Hangfire's monitoring API is synchronous and has no cancellation token, so this call is made from the sampler's
/// own tick and never from a request; it is one small read of one table. It runs through <c>JobStorage</c> because
/// the <c>hangfire</c> schema is closed to every other role (<c>docs/runbooks/hangfire-schema.md</c> §4).
/// </para>
/// </summary>
internal sealed class HangfireWorkerHeartbeatSource(JobStorage storage) : IWorkerHeartbeatSource
{
    public WorkerHeartbeatReading Read()
    {
        var servers = storage.GetMonitoringApi().Servers();

        DateTimeOffset? newest = null;
        foreach (var server in servers)
        {
            if (server.Heartbeat is not { } heartbeat)
            {
                continue;
            }

            var at = new DateTimeOffset(Utc(heartbeat));
            if (newest is not { } kept || at > kept)
            {
                newest = at;
            }
        }

        return new WorkerHeartbeatReading(servers.Count > 0, newest);
    }

    // Hangfire hands back a DateTime whose Kind depends on the storage; an Unspecified one is stamped as UTC,
    // which a test proves against real storage under a non-UTC session time zone.
    private static DateTime Utc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
