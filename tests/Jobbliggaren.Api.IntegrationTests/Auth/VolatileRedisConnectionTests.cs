using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Infrastructure.Auth;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Shouldly;
using Testcontainers.Redis;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// <see cref="VolatileRedisConnection"/> against a real instance, in the three states production can put it
/// in: reachable, never reachable (the Api boots before <c>redis-volatile</c> is up — restart policies ignore
/// <c>depends_on</c>), and reachable then gone. Every one of them has to end as the translated fault, because
/// <c>Program.cs</c> renders only <see cref="StoreUnavailableException"/> as the uniform 503.
/// </summary>
public sealed class VolatileRedisConnectionTests : IAsyncDisposable
{
    // A closed port on loopback: nothing listens on 1. The short timeouts keep the refusal inside a second.
    private const string NeverReachable = "127.0.0.1:1,connectTimeout=500,syncTimeout=500,asyncTimeout=500";

    private readonly RedisContainer _redis = VolatileRedisContainer.FromDeployCompose();

    // Started by the tests that need a live instance only: xUnit builds this class once per test.
    private async Task<string> ReachableAsync()
    {
        await _redis.StartAsync(Ct);
        return $"{VolatileRedisContainer.OperatorConnectionString(_redis)},connectTimeout=1000,syncTimeout=1000,asyncTimeout=1000";
    }

    public async ValueTask DisposeAsync() => await VolatileRedisContainer.DisposeAsync(_redis);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<HealthCheckResult> CheckAsync(VolatileRedisConnection connection) =>
        new VolatileRedisHealthCheck(connection).CheckHealthAsync(new HealthCheckContext(), Ct);

    [Fact]
    public async Task A_reachable_instance_runs_the_operation_and_reports_healthy()
    {
        using var connection = new VolatileRedisConnection(await ReachableAsync());

        (await connection.ExecuteAsync(db => db.PingAsync())).ShouldBeGreaterThan(TimeSpan.Zero);
        (await CheckAsync(connection)).Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task A_never_reachable_instance_constructs_without_throwing_and_its_first_command_is_the_translated_fault()
    {
        // With StackExchange.Redis's default (AbortOnConnectFail = true) the constructor itself would throw,
        // from the DI singleton factory, outside ExecuteAsync — and the caller would get a 500.
        using var connection = new VolatileRedisConnection(NeverReachable);

        var fault = await Should.ThrowAsync<VolatileRedisUnavailableException>(
            () => connection.ExecuteAsync(db => db.PingAsync()));

        fault.ShouldBeAssignableTo<StoreUnavailableException>();
        fault.InnerException.ShouldBeNull();
        fault.InnerType.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_never_reachable_instance_reports_unhealthy_without_throwing()
    {
        using var connection = new VolatileRedisConnection(NeverReachable);

        (await CheckAsync(connection)).Status.ShouldBe(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task An_instance_that_goes_away_reports_unhealthy_and_names_only_the_failure_type()
    {
        using var connection = new VolatileRedisConnection(await ReachableAsync());
        (await CheckAsync(connection)).Status.ShouldBe(HealthStatus.Healthy);

        await _redis.StopAsync(Ct);

        var result = await CheckAsync(connection);
        result.Status.ShouldBe(HealthStatus.Unhealthy);

        // A Redis message can embed the operated key, so neither the exception nor its message travels: the
        // description is one of the check's two fixed sentences, the second carrying a bare type name.
        result.Exception.ShouldBeNull();
        result.Description.ShouldNotBeNull().ShouldMatch(
            @"^(The volatile Redis connection reports no connected endpoint\.|Volatile Redis PING failed \(\w+\)\.)$");
    }
}
