using Jobbliggaren.Api.IntegrationTests.Sessions;
using Jobbliggaren.Application.Landing.Common;
using Jobbliggaren.Infrastructure.Configuration;
using Jobbliggaren.Infrastructure.Landing;
using Shouldly;
using StackExchange.Redis;

namespace Jobbliggaren.Api.IntegrationTests.Security;

public sealed class RedisClientConfigurationContractTests(RedisBoundaryFixture fixture)
    : IClassFixture<RedisBoundaryFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ClientConfiguration_ApplicationIdentities_ConnectAndShareCachePermissions()
    {
        using var api = await ConnectionMultiplexer.ConnectAsync(Options(RedisClientIdentity.ApiPersistent));
        using var worker = await ConnectionMultiplexer.ConnectAsync(Options(RedisClientIdentity.WorkerPersistent));
        using var volatileRedis = await ConnectionMultiplexer.ConnectAsync(Options(RedisClientIdentity.ApiVolatile));
        await api.GetDatabase().PingAsync();
        await worker.GetDatabase().PingAsync();
        await volatileRedis.GetDatabase().PingAsync();

        var expected = new LandingStatsDto(12, 3, false, FakeDateTimeProvider.Now.UtcNow);
        var writer = new RedisLandingStatsCache(RedisBoundaryFixture.Cache(worker));
        var reader = new RedisLandingStatsCache(RedisBoundaryFixture.Cache(api));
        await writer.SetAsync(expected, Ct);
        (await reader.GetAsync(Ct)).ShouldBe(expected);

        var refused = await Should.ThrowAsync<RedisServerException>(() =>
            worker.GetDatabase().HashGetAsync("jobbliggaren:session:synthetic", "data"));
        refused.Message.ShouldContain("NOPERM");
    }

    [Theory]
    [InlineData("CONFIG", "GET", "maxmemory")]
    [InlineData("INFO", "server", null)]
    [InlineData("CLUSTER", "NODES", null)]
    [InlineData("SENTINEL", "MASTERS", null)]
    [InlineData("SUBSCRIBE", "synthetic", null)]
    [InlineData("PSUBSCRIBE", "synthetic*", null)]
    [InlineData("UNSUBSCRIBE", "synthetic", null)]
    [InlineData("PUNSUBSCRIBE", "synthetic*", null)]
    [InlineData("PUBLISH", "synthetic", "payload")]
    [InlineData("SELECT", "1", null)]
    public async Task ClientConfiguration_DisabledCommand_IsRejectedByTheClient(string command, string first, string? second)
    {
        using var api = await ConnectionMultiplexer.ConnectAsync(Options(RedisClientIdentity.ApiPersistent));
        var arguments = second is null ? new object[] { first } : [first, second];
        await Should.ThrowAsync<RedisCommandException>(() => api.GetDatabase().ExecuteAsync(command, arguments));
        await api.GetDatabase().PingAsync();
    }

    private ConfigurationOptions Options(RedisClientIdentity identity)
    {
        var user = identity switch
        {
            RedisClientIdentity.ApiPersistent => RedisBoundaryFixture.ApiPersistent,
            RedisClientIdentity.WorkerPersistent => RedisBoundaryFixture.WorkerPersistent,
            RedisClientIdentity.ApiVolatile => RedisBoundaryFixture.ApiVolatile,
            _ => throw new ArgumentOutOfRangeException(nameof(identity)),
        };
        var store = identity == RedisClientIdentity.ApiVolatile ? fixture.Volatile : fixture.Persistent;
        return RedisClientConfiguration.Create(fixture.OptionsFor(store, user).ToString(true), identity);
    }
}
