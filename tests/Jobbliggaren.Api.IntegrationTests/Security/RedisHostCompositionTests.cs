using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Configuration;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using StackExchange.Redis;

namespace Jobbliggaren.Api.IntegrationTests.Security;

public sealed class RedisHostCompositionTests(RedisBoundaryFixture fixture) : IClassFixture<RedisBoundaryFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Persistent(string user) => fixture.OptionsFor(fixture.Persistent, user).ToString(true);
    private string Volatile() => fixture.OptionsFor(fixture.Volatile, RedisBoundaryFixture.ApiVolatile).ToString(true);

    private static IConfiguration Configuration(string persistent, string? volatileRedis = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Redis"] = persistent,
            ["ConnectionStrings:VolatileRedis"] = volatileRedis,
        }).Build();

    [Fact]
    public async Task ApiComposition_CacheAndReadiness_UseTheRegisteredPersistentConnection()
    {
        using var services = new ServiceCollection().AddLogging()
            .AddApiRedisConnections(Configuration(Persistent(RedisBoundaryFixture.ApiPersistent), Volatile()))
            .BuildServiceProvider();
        await services.RequireApiRedisReadyAsync(Ct);
        var connection = services.GetRequiredService<IConnectionMultiplexer>();
        var options = services.GetRequiredService<IOptions<RedisCacheOptions>>().Value;
        (await options.ConnectionMultiplexerFactory!()).ShouldBeSameAs(connection);
        var cache = services.GetRequiredService<IDistributedCache>();
        await cache.SetStringAsync("session:composition", "synthetic", Ct);
        (await connection.GetDatabase().HashGetAsync("jobbliggaren:session:composition", ["data"]))
            .Single().ToString().ShouldBe("synthetic");
        (await services.GetRequiredService<PersistentRedisReadinessProbe>().CheckAsync(Ct)).ShouldBeTrue();
    }

    [Fact]
    public async Task WorkerComposition_RegistersNoVolatileRoute_AndCanOnlyPublishStatistics()
    {
        using var services = new ServiceCollection().AddLogging()
            .AddWorkerRedisConnection(Configuration(Persistent(RedisBoundaryFixture.WorkerPersistent)))
            .BuildServiceProvider();
        await services.RequireWorkerRedisReadyAsync(Ct);
        services.GetService<VolatileRedisConnection>().ShouldBeNull();
        var cache = services.GetRequiredService<IDistributedCache>();
        await cache.SetStringAsync("landing:stats:v1", "synthetic", Ct);
        await Should.ThrowAsync<RedisServerException>(() => cache.GetStringAsync("session:composition", Ct));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RootDisposal_BeforeOrAfterCacheUse_ClosesTheSharedConnection(bool useCache)
    {
        var services = new ServiceCollection().AddLogging()
            .AddApiRedisConnections(Configuration(Persistent(RedisBoundaryFixture.ApiPersistent), Volatile()))
            .BuildServiceProvider();
        var connection = services.GetRequiredService<IConnectionMultiplexer>();
        if (useCache)
            await services.GetRequiredService<IDistributedCache>().GetStringAsync("session:disposal", Ct);
        services.Dispose();
        connection.IsConnected.ShouldBeFalse();
        services.Dispose();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ApiStartup_WrongPasswordOrStore_RefusesWithSanitizedFailure(bool volatileFailure, bool wrongStore)
    {
        var persistent = Persistent(RedisBoundaryFixture.ApiPersistent);
        var volatileRedis = Volatile();
        var user = volatileFailure ? RedisBoundaryFixture.ApiVolatile : RedisBoundaryFixture.ApiPersistent;
        var target = volatileFailure ? fixture.Volatile : fixture.Persistent;
        if (wrongStore)
            target = volatileFailure ? fixture.Persistent : fixture.Volatile;
        var invalid = fixture.OptionsFor(target, user, wrongStore ? null : "must-not-appear-in-error").ToString(true);
        if (volatileFailure) volatileRedis = invalid;
        else persistent = invalid;
        using var services = new ServiceCollection().AddLogging()
            .AddApiRedisConnections(Configuration(persistent, volatileRedis)).BuildServiceProvider();

        var error = await Should.ThrowAsync<InvalidOperationException>(() => services.RequireApiRedisReadyAsync(Ct));
        error.InnerException.ShouldBeNull();
        error.ToString().ShouldNotContain("must-not-appear-in-error");
        error.ToString().ShouldNotContain(fixture.Password(user));
    }
}
