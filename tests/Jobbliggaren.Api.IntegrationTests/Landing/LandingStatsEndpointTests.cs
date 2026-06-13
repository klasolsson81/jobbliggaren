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
    public async Task GET_landing_stats_cache_miss_returns_floor_with_isStale_true()
    {
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
        json.GetProperty("activeCount").GetInt32().ShouldBeGreaterThan(0);
        json.GetProperty("newToday").GetInt32().ShouldBe(0);
        json.TryGetProperty("refreshedAt", out var refreshedAt).ShouldBeTrue();
        refreshedAt.ValueKind.ShouldBe(JsonValueKind.Null);
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
