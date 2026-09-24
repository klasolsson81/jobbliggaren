using System.Net;
using System.Net.Http.Json;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.IntegrationTests.Security;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Landing.Common;
using Jobbliggaren.Application.Landing.Jobs.RefreshLandingStats;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure.Configuration;
using Jobbliggaren.Infrastructure.Landing;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Landing;

[Collection("Api")]
public sealed class RedisWorkerPublicationTests(ApiFactory factory)
{
    [Fact]
    public async Task WorkerJob_WithRoleBoundComposition_PublishesRealCountsReadByApi()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var calendar = scope.ServiceProvider.GetRequiredService<ISwedishCalendar>();
        var boundary = factory.RedisBoundary;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Redis"] = boundary.OptionsFor(boundary.Persistent, RedisBoundaryFixture.WorkerPersistent).ToString(true),
        }).Build();
        await using var worker = new ServiceCollection().AddLogging()
            .AddWorkerRedisConnection(configuration)
            .AddSingleton<IAppDbContext>(db)
            .AddSingleton(clock)
            .AddSingleton(calendar)
            .AddSingleton<ILandingStatsCache, RedisLandingStatsCache>()
            .AddTransient<RefreshLandingStatsJob>()
            .BuildServiceProvider();
        await worker.RequireWorkerRedisReadyAsync(ct);

        // Expiry removes any earlier publication; only the real job below supplies the next value.
        await boundary.PersistentAdmin.GetDatabase().KeyExpireAsync("jobbliggaren:landing:stats:v1", TimeSpan.Zero);
        var before = clock.UtcNow;
        await worker.GetRequiredService<RefreshLandingStatsJob>().RunAsync(ct);
        var after = clock.UtcNow;

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/v1/landing/stats", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var stats = await response.Content.ReadFromJsonAsync<LandingStatsDto>(ct);
        stats.ShouldNotBeNull();
        stats.IsStale.ShouldBeFalse();
        stats.ActiveCount.ShouldNotBeNull();
        stats.ActiveCount.Value.ShouldBeGreaterThanOrEqualTo(0);
        stats.NewToday.ShouldNotBeNull();
        stats.NewToday.Value.ShouldBeGreaterThanOrEqualTo(0);
        stats.RefreshedAt.ShouldNotBeNull();
        stats.RefreshedAt.Value.ShouldBeGreaterThanOrEqualTo(before);
        stats.RefreshedAt.Value.ShouldBeLessThanOrEqualTo(after);
    }
}
