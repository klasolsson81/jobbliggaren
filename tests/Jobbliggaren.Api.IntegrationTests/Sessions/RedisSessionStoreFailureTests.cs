using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Auth.Sessions;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Options;
using Shouldly;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Jobbliggaren.Api.IntegrationTests.Sessions;

public class RedisSessionStoreFailureTests : IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder("redis:8-alpine").Build();

    private RedisSessionStore _store = null!;
    private Session _existingSession = null!;
    private ConnectionMultiplexer _mux = null!;

    public async ValueTask InitializeAsync()
    {
        await _redis.StartAsync();

        var connectionString = $"{_redis.GetConnectionString()},connectTimeout=1000,syncTimeout=1000";

        var cache = new RedisCache(Options.Create(
            new RedisCacheOptions
            {
                Configuration = connectionString,
                InstanceName = "jobbliggaren:",
            }));

        _mux = (ConnectionMultiplexer)await ConnectionMultiplexer.ConnectAsync(connectionString);

        _store = new RedisSessionStore(
            cache,
            _mux,
            FakeDateTimeProvider.Now,
            Options.Create(new SessionStoreOptions { Ttl = TimeSpan.FromDays(14) }));

        _existingSession = await _store.CreateAsync(Guid.NewGuid(), default);
    }

    public async ValueTask DisposeAsync()
    {
        await _mux.CloseAsync();
        _mux.Dispose();
        await _redis.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task GetAsync_ShouldThrowSessionStoreUnavailableException_WhenRedisIsDown()
    {
        var ct = TestContext.Current.CancellationToken;
        await _redis.StopAsync(ct);

        await Should.ThrowAsync<SessionStoreUnavailableException>(
            () => _store.GetAsync(_existingSession.Id, ct));
    }

    [Fact]
    public async Task CreateAsync_ShouldThrowSessionStoreUnavailableException_WhenRedisIsDown()
    {
        var ct = TestContext.Current.CancellationToken;
        await _redis.StopAsync(ct);

        await Should.ThrowAsync<SessionStoreUnavailableException>(
            () => _store.CreateAsync(Guid.NewGuid(), ct));
    }

    [Fact]
    public async Task InvalidateAsync_ShouldThrowSessionStoreUnavailableException_WhenRedisIsDown()
    {
        var ct = TestContext.Current.CancellationToken;
        await _redis.StopAsync(ct);

        await Should.ThrowAsync<SessionStoreUnavailableException>(
            () => _store.InvalidateAsync(_existingSession.Id, ct));
    }

    [Fact]
    public async Task GetAsync_ShouldNotLeakRawRedisConnectionException_WhenRedisIsDown()
    {
        var ct = TestContext.Current.CancellationToken;
        await _redis.StopAsync(ct);

        var ex = await Should.ThrowAsync<Exception>(
            () => _store.GetAsync(_existingSession.Id, ct));

        ex.ShouldBeOfType<SessionStoreUnavailableException>();
        ex.ShouldNotBeOfType<RedisConnectionException>();
    }
}
