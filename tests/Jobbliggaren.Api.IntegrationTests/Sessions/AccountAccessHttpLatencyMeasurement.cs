using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Sessions;

/// <summary>
/// Requested #1976 diagnostic: authenticated HTTP round trips over an actual loopback Kestrel listener,
/// including the primary-backed account/session guard. ADR 0045 budgets handler latency; these local
/// round trips are a proxy observation only. No duration is asserted and no fitness ratchet is added.
/// </summary>
[Collection("Api")]
public sealed class AccountAccessHttpLatencyMeasurement(ApiFactory factory, ITestOutputHelper output)
{
    private const int WarmupRequests = 20;
    private const int MeasuredRequests = 100;
    private const double ReadQueryReferenceP95Milliseconds = 300;

    [Fact]
    public async Task AuthenticatedRead_ShouldMeasureActualHttpRoundTrips_WhenTheLiveSessionIsAccepted()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = factory.WithWebHostBuilder(_ => { });
        host.UseKestrel(0);
        using var startupClient = host.CreateClient();

        var server = host.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>().ShouldNotBeNull();
        var listener = addresses.Addresses.Select(address => new Uri(address, UriKind.Absolute))
            .Where(address => address.Scheme == Uri.UriSchemeHttp && address.IsLoopback && address.Port > 0)
            .ShouldHaveSingleItem();

        // A socket handler, rather than TestServer's in-process dispatch, makes the measurement real HTTP.
        using var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false })
        {
            BaseAddress = listener,
        };
        using (var anonymous = await client.GetAsync("/api/v1/me", ct))
            anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var email = $"latency-{Guid.NewGuid():N}@example.test";
        var rawSession = await AuthTestHelpers.RegisterAndGetSessionIdAsync(host, email,
            SessionLifetime.Persistent, ct);
        Session original;
        await using (var read = host.Services.CreateAsyncScope())
        {
            original = (await read.ServiceProvider.GetRequiredService<ISessionStore>()
                .GetAsync(SessionId.FromRaw(rawSession), ct)).ShouldNotBeNull();
        }
        original.Lifetime.ShouldBe(SessionLifetime.Persistent);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", original.Id.Reveal());

        for (var sample = 0; sample < WarmupRequests; sample++)
            await SuccessfulRoundTripAsync(client, ct);

        var samples = new double[MeasuredRequests];
        for (var sample = 0; sample < samples.Length; sample++)
            samples[sample] = await SuccessfulRoundTripAsync(client, ct);
        Array.Sort(samples);

        await using (var read = host.Services.CreateAsyncScope())
        {
            var current = (await read.ServiceProvider.GetRequiredService<ISessionStore>()
                .GetAsync(original.Id, ct)).ShouldNotBeNull();
            current.Lifetime.ShouldBe(original.Lifetime);
            current.AccessRevision.ShouldBe(original.AccessRevision);
        }

        var p50 = Percentile(samples, 0.50);
        var p95 = Percentile(samples, 0.95);
        var p99 = Percentile(samples, 0.99);
        output.WriteLine($"OBSERVE-ONLY authenticated HTTP: warmup={WarmupRequests}, samples={MeasuredRequests}, all=200.");
        output.WriteLine(FormattableString.Invariant($"p50_ms={p50:F3}; p95_ms={p95:F3}; p99_ms={p99:F3}."));
        var classification = p95 <= ReadQueryReferenceP95Milliseconds ? "within_reference" : "above_reference";
        output.WriteLine($"ADR 0045 class (a) p95 reference=300 ms; local_proxy={classification}; no latency assertion.");
        output.WriteLine("Limitations: whole loopback HTTP round trip with buffered response, primary PostgreSQL and real Redis; "
            + "small fixture on shared local hardware, Development host, raised fixture rate limits, warmed connections; "
            + "no TLS, public network or concurrent load. This is not server-handler timing or production-scale evidence; "
            + "it does not replace the existing NBomber suite.");
    }

    private static async Task<double> SuccessfulRoundTripAsync(HttpClient client, CancellationToken ct)
    {
        var elapsed = Stopwatch.StartNew();
        using var response = await client.GetAsync("/api/v1/me", HttpCompletionOption.ResponseContentRead, ct);
        elapsed.Stop();
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return elapsed.Elapsed.TotalMilliseconds;
    }

    private static double Percentile(double[] sorted, double percentile) =>
        sorted[(int)Math.Ceiling(percentile * sorted.Length) - 1];
}
