using Jobbliggaren.Application.Admin.ServiceObservations;
using Jobbliggaren.Infrastructure.Auth;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Jobbliggaren.Api.HealthChecks;

/// <summary>
/// Whether the volatile Redis answers, by running the readiness check that is already registered for it
/// (#1982, ADR 0159). The check's own documentation rules out a second public route to the connection, so this
/// goes through <see cref="HealthCheckService"/> and filters on the check's public name. Only the verdict
/// crosses the port: the check's description carries a latency, and its exception text is never forwarded.
/// A check that is missing from the report, or that reports anything but Healthy or Unhealthy, was not a
/// measurement, so the probe throws and the sampler shows the row as unknown.
/// </summary>
internal sealed class ReadinessVolatileStoreProbe(HealthCheckService health) : IVolatileStoreProbe
{
    public async Task<bool> IsReachableAsync(CancellationToken cancellationToken)
    {
        var report = await health.CheckHealthAsync(
            registration => registration.Name == VolatileRedisHealthCheckExtensions.HealthCheckName,
            cancellationToken);

        if (!report.Entries.TryGetValue(VolatileRedisHealthCheckExtensions.HealthCheckName, out var entry))
        {
            throw new InvalidOperationException("The volatile Redis readiness check is not registered.");
        }

        return entry.Status switch
        {
            HealthStatus.Healthy => true,
            HealthStatus.Unhealthy => false,
            _ => throw new InvalidOperationException("The volatile Redis readiness check gave no verdict."),
        };
    }
}
