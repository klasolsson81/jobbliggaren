using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// #616 — resilience pins for the REAL <c>AddBreachedPasswordCheck</c> wiring (typed client +
/// Polly pipeline) against a WireMock HIBP stand-in. This is the only place the CTO-bound
/// FORK 3 mechanics are exercised through the actual DI registration instead of the client's
/// catch branches: the ~TimeoutSeconds attempt budget, ZERO retries, the circuit breaker
/// failing fast without further egress, and the DI-set protocol headers (Add-Padding +
/// User-Agent) reaching the wire.
/// </summary>
/// <remarks>
/// Uses a local DI container rather than ApiFactory (JobTechStreamResilienceTests parity): the
/// resilience pipeline is registered independently of the Identity/Postgres stack, and ApiFactory
/// deliberately swaps <see cref="IBreachedPasswordChecker"/> for a stub.
/// </remarks>
public class HibpBreachCheckResilienceTests
{
    // SHA-1("password") prefix/suffix — publicly known digest, not a secret. gitleaks:allow
    private const string Suffix = "1E4C9B93F3F0682250B6CF8331B7EE68FD8";

    /// <summary>
    /// WireMock's FIRST request pays JIT + localhost connection setup that can exceed the 1 s
    /// pipeline budget on Windows, tripping OnTimeout before the wire is reached. Pay that cost
    /// with a throwaway request outside the pipeline, then clear the log so assertions only see
    /// pipeline traffic.
    /// </summary>
    private static async Task WarmUpAsync(WireMockServer server, CancellationToken ct)
    {
        using var warmup = new HttpClient();
        await warmup.GetAsync($"{server.Url}/warmup", ct);
        server.ResetLogEntries();
    }

    [Fact]
    public async Task CheckAsync_ThroughRealPipeline_SendsProtocolHeaders_AndMatchesBreached()
    {
        var ct = TestContext.Current.CancellationToken;
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/range/*").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody($"{Suffix}:42"));

        await WarmUpAsync(server, ct);
        await using var provider = BuildProvider(server.Url!);
        var checker = provider.GetRequiredService<IBreachedPasswordChecker>();

        var verdict = await checker.CheckAsync("password", ct);

        verdict.ShouldBe(BreachCheckVerdict.Breached);
        var request = server.LogEntries.ShouldHaveSingleItem().RequestMessage;
        request.Path.ShouldBe("/range/5BAA6");
        request.Headers!["Add-Padding"].ToString().ShouldBe("true");
        request.Headers.ShouldContainKey("User-Agent");
    }

    [Fact]
    public async Task CheckAsync_Http500ThroughRealPipeline_ReturnsUnavailable_WithoutRetry()
    {
        var ct = TestContext.Current.CancellationToken;
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/range/*").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(500));

        await WarmUpAsync(server, ct);
        await using var provider = BuildProvider(server.Url!);
        var checker = provider.GetRequiredService<IBreachedPasswordChecker>();

        var verdict = await checker.CheckAsync("password", ct);

        verdict.ShouldBe(BreachCheckVerdict.Unavailable);
        // Retry 0 (CTO-bind FORK 3): exactly ONE request hit the wire — a failed attempt goes
        // straight to fail-open instead of delaying a waiting user.
        server.LogEntries.Count.ShouldBe(1);
    }

    [Fact]
    public async Task CheckAsync_DelayBeyondTimeoutBudget_ReturnsUnavailable()
    {
        var ct = TestContext.Current.CancellationToken;
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/range/*").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody($"{Suffix}:42")
                .WithDelay(TimeSpan.FromSeconds(5)));

        await WarmUpAsync(server, ct);
        await using var provider = BuildProvider(server.Url!);
        var checker = provider.GetRequiredService<IBreachedPasswordChecker>();

        // TimeoutSeconds=1 in this fixture: the pipeline timeout must fire long before the 5 s
        // body arrives and classify as fail-open, not hang the interactive caller.
        var verdict = await checker.CheckAsync("password", ct);

        verdict.ShouldBe(BreachCheckVerdict.Unavailable);
    }

    [Fact]
    public async Task CheckAsync_AfterConsecutiveFailures_CircuitOpens_AndFailsFastWithoutEgress()
    {
        var ct = TestContext.Current.CancellationToken;
        using var server = WireMockServer.Start();
        server
            .Given(Request.Create().WithPath("/range/*").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(500));

        await WarmUpAsync(server, ct);
        await using var provider = BuildProvider(server.Url!);
        var checker = provider.GetRequiredService<IBreachedPasswordChecker>();

        // MinimumThroughput=2, FailureRatio=0.5 in this fixture: drive failures until the
        // breaker opens (bounded loop — must open well within it).
        var opened = false;
        for (var i = 0; i < 10 && !opened; i++)
        {
            (await checker.CheckAsync("password", ct)).ShouldBe(BreachCheckVerdict.Unavailable);
            var before = server.LogEntries.Count;
            (await checker.CheckAsync("password", ct)).ShouldBe(BreachCheckVerdict.Unavailable);
            opened = server.LogEntries.Count == before;
        }

        opened.ShouldBeTrue("circuit never opened — every call kept reaching the wire");

        // Open circuit = instant fail-open with ZERO further egress (BreakDuration 60 s).
        var atOpen = server.LogEntries.Count;
        (await checker.CheckAsync("password", ct)).ShouldBe(BreachCheckVerdict.Unavailable);
        server.LogEntries.Count.ShouldBe(atOpen);
    }

    private static ServiceProvider BuildProvider(string baseUrl)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BreachCheck:BaseUrl"] = baseUrl + "/",
                ["BreachCheck:TimeoutSeconds"] = "1",
                ["BreachCheck:CircuitBreakerMinimumThroughput"] = "2",
                ["BreachCheck:CircuitBreakerFailureRatio"] = "0.5",
                ["BreachCheck:CircuitBreakerSamplingSeconds"] = "5",
                ["BreachCheck:CircuitBreakerBreakSeconds"] = "60",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBreachedPasswordCheck(configuration);
        return services.BuildServiceProvider();
    }
}
