using Jobbliggaren.Api.HealthChecks;
using Jobbliggaren.Infrastructure.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Admin;

/// <summary>
/// #1982 (ADR 0159) — the volatile-store probe runs the readiness check that is already registered and forwards a
/// verdict, nothing else. The first group uses the real <see cref="HealthCheckService"/> with stand-in checks, because
/// what is under test is the probe's filter by name and what it makes of a verdict; the real Redis check is exercised
/// by the readiness tests against a real Redis. Unhealthy is the verdict the real check itself emits when its PING
/// fails, so a stub that returns it is a state production produces.
/// </summary>
public sealed class ReadinessVolatileStoreProbeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ReadinessVolatileStoreProbe Probe(params (string Name, HealthStatus Status)[] checks)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = services.AddHealthChecks();
        foreach (var (name, status) in checks)
        {
            builder.AddCheck(name, () => new HealthCheckResult(status, description: "PING=1.2ms host=secret-redis"));
        }

        return new ReadinessVolatileStoreProbe(services.BuildServiceProvider().GetRequiredService<HealthCheckService>());
    }

    [Fact]
    public async Task IsReachable_AHealthyCheck_IsTrue()
    {
        (await Probe((VolatileRedisHealthCheckExtensions.HealthCheckName, HealthStatus.Healthy)).IsReachableAsync(Ct)).ShouldBeTrue();
    }

    [Fact]
    public async Task IsReachable_AnUnhealthyCheck_IsFalse()
    {
        (await Probe((VolatileRedisHealthCheckExtensions.HealthCheckName, HealthStatus.Unhealthy)).IsReachableAsync(Ct)).ShouldBeFalse();
    }

    [Theory]
    [InlineData("postgres", HealthStatus.Unhealthy)]
    [InlineData("redis", HealthStatus.Unhealthy)]
    public async Task IsReachable_TheVerdictOfAnotherCheckNeverDecidesIt(string otherName, HealthStatus otherStatus)
    {
        var probe = Probe((otherName, otherStatus), (VolatileRedisHealthCheckExtensions.HealthCheckName, HealthStatus.Healthy));

        (await probe.IsReachableAsync(Ct)).ShouldBeTrue();
    }

    [Fact]
    public async Task IsReachable_RunsOnlyTheVolatileCheck_NeverThePostgresOrTheSessionRedisCheck()
    {
        // Every tick would otherwise also run the checks that /api/ready runs for the other stores.
        var services = new ServiceCollection();
        services.AddLogging();
        var otherCalls = 0;
        services.AddHealthChecks()
            .AddCheck("postgres", () => { otherCalls++; return HealthCheckResult.Healthy(); })
            .AddCheck("redis", () => { otherCalls++; return HealthCheckResult.Healthy(); })
            .AddCheck(VolatileRedisHealthCheckExtensions.HealthCheckName, () => HealthCheckResult.Healthy());
        var probe = new ReadinessVolatileStoreProbe(services.BuildServiceProvider().GetRequiredService<HealthCheckService>());

        (await probe.IsReachableAsync(Ct)).ShouldBeTrue();

        otherCalls.ShouldBe(0);
    }

    [Fact]
    public async Task IsReachable_ACheckThatIsNotRegistered_IsNotAMeasurement()
    {
        var probe = Probe(("postgres", HealthStatus.Healthy));

        await Should.ThrowAsync<InvalidOperationException>(async () => await probe.IsReachableAsync(Ct));
    }

    [Fact]
    public async Task IsReachable_ADegradedCheck_IsNotAMeasurementEither()
    {
        // The real check never answers Degraded, so a Degraded here would be a different check under the same name.
        var probe = Probe((VolatileRedisHealthCheckExtensions.HealthCheckName, HealthStatus.Degraded));

        await Should.ThrowAsync<InvalidOperationException>(async () => await probe.IsReachableAsync(Ct));
    }

    [Fact]
    public async Task IsReachable_TheCallersTokenReachesTheCheck()
    {
        var health = Substitute.For<HealthCheckService>();
        health.CheckHealthAsync(Arg.Any<Func<HealthCheckRegistration, bool>>(), Arg.Any<CancellationToken>())
            .Returns<Task<HealthReport>>(call => Task.FromCanceled<HealthReport>(call.Arg<CancellationToken>()));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await new ReadinessVolatileStoreProbe(health).IsReachableAsync(cancelled.Token));
    }
}
