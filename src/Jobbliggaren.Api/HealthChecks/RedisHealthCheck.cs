using Jobbliggaren.Infrastructure.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Jobbliggaren.Api.HealthChecks;

internal sealed class RedisHealthCheck(PersistentRedisReadinessProbe probe) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await probe.CheckAsync(cancellationToken)
            ? HealthCheckResult.Healthy("Persistent Redis is available.")
            : HealthCheckResult.Unhealthy("Persistent Redis is unavailable.");
}
