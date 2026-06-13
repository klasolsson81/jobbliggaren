using System.Security.Cryptography;
using System.Text;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Auth.Sessions;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Options;
using Shouldly;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Jobbliggaren.Api.IntegrationTests.Sessions;

public class RedisSessionStoreTtlTests : IAsyncLifetime
{
    private const int ShortTtlSeconds = 3;

    private readonly RedisContainer _redis = new RedisBuilder("redis:8-alpine").Build();

    private RedisSessionStore _shortTtlStore = null!;
    private RedisSessionStore _defaultStore = null!;
    private IDatabase _db = null!;
    private ConnectionMultiplexer _mux = null!;
    private FakeDateTimeProvider _time = null!;

    public async ValueTask InitializeAsync()
    {
        await _redis.StartAsync();

        var cs = _redis.GetConnectionString();
        _mux = (ConnectionMultiplexer)await ConnectionMultiplexer.ConnectAsync(cs);
        _db = _mux.GetDatabase();

        var cache = new RedisCache(Options.Create(
            new RedisCacheOptions { Configuration = cs, InstanceName = "jobbliggaren:" }));

        _time = FakeDateTimeProvider.Now;

        _shortTtlStore = new RedisSessionStore(
            cache,
            _mux,
            _time,
            Options.Create(new SessionStoreOptions { Ttl = TimeSpan.FromSeconds(ShortTtlSeconds) }));

        _defaultStore = new RedisSessionStore(
            cache,
            _mux,
            _time,
            Options.Create(new SessionStoreOptions { Ttl = TimeSpan.FromDays(14) }));
    }

    public async ValueTask DisposeAsync()
    {
        await _mux.CloseAsync();
        _mux.Dispose();
        await _redis.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task CreateAsync_ShouldSetRedisTtlToApproximately14Days_WhenCalled()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = await _defaultStore.CreateAsync(Guid.NewGuid(), ct);

        var ttl = await _db.KeyTimeToLiveAsync(RedisKey(session.Id));

        ttl.ShouldNotBeNull();
        ttl!.Value.TotalSeconds.ShouldBeInRange(
            TimeSpan.FromDays(14).TotalSeconds - 30,
            TimeSpan.FromDays(14).TotalSeconds + 30);
    }

    [Fact]
    public async Task GetAsync_ShouldResetRedisTtlToFullWindow_WhenSessionExists()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = await _shortTtlStore.CreateAsync(Guid.NewGuid(), ct);

        // Wait past half the TTL so the remaining TTL is visibly shorter
        await Task.Delay(TimeSpan.FromSeconds(ShortTtlSeconds / 2.0 + 0.5), ct);

        var ttlBeforeGet = await _db.KeyTimeToLiveAsync(RedisKey(session.Id));

        await _shortTtlStore.GetAsync(session.Id, ct);

        var ttlAfterGet = await _db.KeyTimeToLiveAsync(RedisKey(session.Id));

        ttlAfterGet.ShouldNotBeNull();
        ttlAfterGet!.Value.ShouldBeGreaterThan(ttlBeforeGet!.Value);
    }

    [Fact]
    public async Task GetAsync_ShouldReturnNull_WhenSessionExpiredInRedis()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = await _shortTtlStore.CreateAsync(Guid.NewGuid(), ct);

        await Task.Delay(TimeSpan.FromSeconds(ShortTtlSeconds + 1), ct);

        var result = await _shortTtlStore.GetAsync(session.Id, ct);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task InvalidateAsync_ShouldRemoveKeyImmediately_WhenCalled()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = await _defaultStore.CreateAsync(Guid.NewGuid(), ct);

        await _defaultStore.InvalidateAsync(session.Id, ct);

        var exists = await _db.KeyExistsAsync(RedisKey(session.Id));
        exists.ShouldBeFalse();
    }

    // Computes the full Redis key as stored by RedisSessionStore + IDistributedCache prefix
    private static string RedisKey(SessionId sessionId)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(sessionId.Reveal()), hash);
        var hashed = Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"jobbliggaren:session:{hashed}";
    }
}
