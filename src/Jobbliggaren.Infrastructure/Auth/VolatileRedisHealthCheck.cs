using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Jobbliggaren.Infrastructure.Auth;

/// <summary>
/// Readiness for the volatile Redis instance. It lives here rather than beside the Api's
/// <c>RedisHealthCheck</c> because <see cref="VolatileRedisConnection"/> is internal and
/// <see cref="VolatileRedisConnection.ExecuteAsync{T}"/> is its only route to the database: a check in the Api
/// would need a second, public route, and that route would not translate faults.
/// </summary>
internal sealed class VolatileRedisHealthCheck(VolatileRedisConnection redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!redis.IsConnected)
            return HealthCheckResult.Unhealthy("The volatile Redis connection reports no connected endpoint.");

        try
        {
            var latency = await redis.ExecuteAsync(db => db.PingAsync()).WaitAsync(cancellationToken);
            return HealthCheckResult.Healthy($"Volatile Redis OK (PING={latency.TotalMilliseconds:F1}ms).");
        }
        catch (VolatileRedisUnavailableException ex)
        {
            // The type name only, like every other surface of this fault: a Redis message can embed a key.
            return HealthCheckResult.Unhealthy($"Volatile Redis PING failed ({ex.InnerType}).");
        }
    }
}

/// <summary>The one public seam onto the volatile instance's readiness, for the Api's composition root.</summary>
public static class VolatileRedisHealthCheckExtensions
{
    /// <summary>The check's name in <c>/api/ready</c>'s set, and the deploy stack's service name.</summary>
    public const string HealthCheckName = "redis-volatile";

    /// <summary>The tag <c>/api/ready</c> filters on (<c>Program.cs</c>).</summary>
    public const string ReadyTag = "ready";

    public static IHealthChecksBuilder AddVolatileRedisCheck(this IHealthChecksBuilder builder) =>
        builder.AddCheck<VolatileRedisHealthCheck>(HealthCheckName, tags: [ReadyTag]);
}
