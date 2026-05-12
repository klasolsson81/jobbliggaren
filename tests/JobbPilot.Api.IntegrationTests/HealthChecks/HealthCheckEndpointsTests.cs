using JobbPilot.Api.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace JobbPilot.Api.IntegrationTests.HealthChecks;

/// <summary>
/// TD-29 / F2-P6 — strict readiness-probe-split.
///
/// Verifierar att <c>/api/live</c> och <c>/api/ready</c> är separata endpoints
/// med olika semantik:
/// <list type="bullet">
///   <item><c>/api/live</c>: 200 så länge processen är upp (ingen DB/Redis-check).</item>
///   <item><c>/api/ready</c>: 200 när Postgres + Redis svarar (DbContext-check + Redis-PING).</item>
/// </list>
///
/// Förutsättning: <see cref="ApiFactory"/> startar Testcontainers Postgres +
/// Redis och migrerar AppDbContext + AppIdentityDbContext. När fixturen är
/// redo ska båda endpoints returnera 200.
///
/// 503-vägen (när Postgres eller Redis är nere) testas inte här — Testcontainers
/// har redan startat båda i InitializeAsync, och att stänga dem mid-test
/// race:ar med xunit-parallelism. Manuell verifiering vid Fas 4 dogfood
/// dokumenterad i F2-P4-runbook (aws-cost-recovery.md test-procedur).
/// </summary>
[Collection("Api")]
public sealed class HealthCheckEndpointsTests
{
    private readonly ApiFactory _factory;

    public HealthCheckEndpointsTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task ApiLive_ReturnsHealthy_WhenProcessIsUp()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/live", ct);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(ct);
        body.ShouldBe("Healthy");
    }

    [Fact]
    public async Task ApiReady_ReturnsHealthy_WhenDatabaseAndRedisAreReachable()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/ready", ct);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(ct);
        body.ShouldBe("Healthy");
    }

    [Fact]
    public async Task ApiLive_DoesNotEvaluateRegisteredChecks()
    {
        // /api/live har Predicate _ => false — inga registered checks körs.
        // Verifiera att svaret kommer snabbt (under 500ms) eftersom ingen
        // DB/Redis-roundtrip sker. Anti-regression om någon råkar tagga
        // checks utan "ready" så att /api/live börjar evaluera dem.
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var response = await client.GetAsync("/api/live", ct);
        stopwatch.Stop();

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        // Generös marginal: cold .NET test-host kan ta 50-80ms för första request.
        stopwatch.ElapsedMilliseconds.ShouldBeLessThan(500);
    }

    [Fact]
    public async Task ApiReady_IsAnonymouslyAccessible()
    {
        // ALB target-group anropar /api/ready UTAN auth — endpoint får inte
        // gate:as av RequireAuthorization. Anti-regression-test: om någon
        // glömmer .AllowAnonymous() vid framtida endpoint-grupp-refactor
        // failar detta test eftersom 401 skiljer sig från 200.
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/ready", ct);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        response.Headers.WwwAuthenticate.ShouldBeEmpty();
    }

    [Fact]
    public async Task ApiLive_IsAnonymouslyAccessible()
    {
        // Samma anti-regression-disciplin för /api/live.
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/live", ct);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        response.Headers.WwwAuthenticate.ShouldBeEmpty();
    }

    [Fact]
    public async Task LegacyHealthEndpoint_IsRemoved()
    {
        // /health-endpoint togs bort i F2-P6 (TD-29-stängning). Behöll inte
        // legacy-alias eftersom ingen konsument refererade det utöver Program.cs
        // själv. Anti-regression-test: om någon råkar lägga tillbaka /health
        // utan att uppdatera detta test signalerar fail att docs/ALB-konfig
        // behöver synkas.
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health", ct);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound);
    }
}
