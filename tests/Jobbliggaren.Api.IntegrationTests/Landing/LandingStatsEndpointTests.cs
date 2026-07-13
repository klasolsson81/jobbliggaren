using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Landing.Common;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Landing;

[Collection("Api")]
public class LandingStatsEndpointTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GET_landing_stats_anonymous_returns_200()
    {
        var ct = TestContext.Current.CancellationToken;

        // Ingen Authorization-header sätts — verifierar publik anonym åtkomst.
        var response = await _client.GetAsync("/api/v1/landing/stats", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GET_landing_stats_cache_miss_returns_null_counts_never_a_fabricated_number()
    {
        // REGRESSION (CTO-bind 2026-07-13, A′). Detta test pinnade tidigare GOLVET:
        //     json.GetProperty("activeCount").GetInt32().ShouldBeGreaterThan(0);
        // dvs. det pinnade att wire:n vid cache-miss bar en siffra ingen mätt (40 000), och
        // landningssidan renderade den som ett faktum för varje anonym besökare. Golvets försvar ("vi
        // ljuger inte uppåt") höll bara medan korpusen råkade överstiga 40 000 — den var 40 281 när
        // detta skrevs, en marginal på 0,7 %, och krympande.
        //
        // Detta är den ENDA pinnen på kontraktsgränsen där besökaren faktiskt möter talet, så här måste
        // "vi vet inte" vara JSON null — inte 0, inte ett golv.
        var ct = TestContext.Current.CancellationToken;

        // Rensa Redis-nyckeln explicit så vi vet att vi testar cache-miss-banan.
        // IDistributedCache.InstanceName ("jobbliggaren:") prefix:as automatiskt —
        // skicka enbart logiska nyckeln som RedisLandingStatsCache använder
        // (annars blir nyckeln dubbel-prefixad "jobbliggaren:jobbliggaren:..." och
        // raderingen blir no-op när en annan test-ordning lämnar kvar värde).
        using var scope = _factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        await cache.RemoveAsync("landing:stats:v1", ct);

        var response = await _client.GetAsync("/api/v1/landing/stats", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("isStale").GetBoolean().ShouldBeTrue();

        // Nycklarna MÅSTE finnas med explicit null (inte utelämnas): FE:s zod använder `.nullable()`,
        // vilket kräver att nyckeln är närvarande.
        json.GetProperty("activeCount").ValueKind.ShouldBe(JsonValueKind.Null);
        json.GetProperty("newToday").ValueKind.ShouldBe(JsonValueKind.Null);
        json.TryGetProperty("refreshedAt", out var refreshedAt).ShouldBeTrue();
        refreshedAt.ValueKind.ShouldBe(JsonValueKind.Null);

        // Ett omätt svar får aldrig pinnas i en delad cache: Workern kan fylla cachen sekunden efter,
        // och en CDN skulle annars servera "vet inte" i 30 s till.
        response.Headers.CacheControl?.NoStore.ShouldBeTrue();
    }

    [Fact]
    public async Task GET_landing_stats_cache_hit_returns_worker_written_values()
    {
        var ct = TestContext.Current.CancellationToken;

        // Simulera Worker-write till cache. Hela handler-mekaniken är cache-only —
        // ingen DB-träff sker i request-loopen oavsett vad som ligger i DB:n.
        using var scope = _factory.Services.CreateScope();
        var landingCache = scope.ServiceProvider.GetRequiredService<ILandingStatsCache>();
        var refreshedAt = new DateTimeOffset(2026, 5, 23, 12, 0, 0, TimeSpan.Zero);
        var stats = new LandingStatsDto(
            ActiveCount: 12_345,
            NewToday: 67,
            IsStale: false,
            RefreshedAt: refreshedAt);
        await landingCache.SetAsync(stats, ct);

        var response = await _client.GetAsync("/api/v1/landing/stats", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("activeCount").GetInt32().ShouldBe(12_345);
        json.GetProperty("newToday").GetInt32().ShouldBe(67);
        json.GetProperty("isStale").GetBoolean().ShouldBeFalse();
    }
}
