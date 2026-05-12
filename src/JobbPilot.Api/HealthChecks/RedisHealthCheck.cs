using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace JobbPilot.Api.HealthChecks;

/// <summary>
/// Strict readiness-check för Redis-anslutning (TD-29 / F2-P6).
/// Verifierar att <see cref="IConnectionMultiplexer"/> har en levande
/// anslutning till Redis-clustret innan ALB target-group registrerar
/// tasken som healthy.
///
/// Custom IHealthCheck istället för Xabaril-paket: undviker third-party-
/// dep, semantiken är trivialt två linjer (IsConnected + PING). Per CTO-
/// pattern STEG 13b "use platform features, not familiar tools".
///
/// Tagged "ready" i AddHealthChecks-registreringen i Program.cs så
/// /api/ready-endpoint filtrerar in den, men /api/live filtrerar ut den
/// (predicate _ => false = bara process-status).
/// </summary>
internal sealed class RedisHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer _multiplexer;

    public RedisHealthCheck(IConnectionMultiplexer multiplexer) => _multiplexer = multiplexer;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!_multiplexer.IsConnected)
            return HealthCheckResult.Unhealthy("Redis ConnectionMultiplexer rapporterar IsConnected=false.");

        try
        {
            // PING via default DB. PingAsync kastar vid timeout/unreachable.
            var latency = await _multiplexer.GetDatabase().PingAsync().WaitAsync(cancellationToken);
            return HealthCheckResult.Healthy($"Redis OK (PING={latency.TotalMilliseconds:F1}ms).");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis PING failade.", ex);
        }
    }
}
