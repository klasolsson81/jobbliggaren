using System.Security.Cryptography;
using System.Text;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Auth.Sessions;
using Microsoft.Extensions.Caching.Distributed;
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
    private RedisCache _cache = null!;
    private IDatabase _db = null!;

    public async ValueTask InitializeAsync()
    {
        await _redis.StartAsync();

        var connectionString = $"{_redis.GetConnectionString()},connectTimeout=1000,syncTimeout=1000";

        _cache = new RedisCache(Options.Create(
            new RedisCacheOptions
            {
                Configuration = connectionString,
                InstanceName = "jobbliggaren:",
            }));

        _mux = (ConnectionMultiplexer)await ConnectionMultiplexer.ConnectAsync(connectionString);
        _db = _mux.GetDatabase();

        _store = new RedisSessionStore(
            _cache,
            _mux,
            FakeDateTimeProvider.Now,
            Options.Create(new SessionStoreOptions { Legacy = new SessionLifetimeProfile { SlidingTtl = TimeSpan.FromDays(14) } }));

        _existingSession = await _store.CreateAsync(Guid.NewGuid(), SessionLifetime.Legacy, default);
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
            () => _store.CreateAsync(Guid.NewGuid(), SessionLifetime.Legacy, ct));
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

    // ── Corrupt payload (#511) ───────────────────────────────────────────────
    // A stored payload that no longer deserializes (Redis data corruption, a truncated
    // write, a foreign writer on the key namespace) must be treated as an INVALID session,
    // exactly like a missing key — never a thrown JsonException. Before the guard, the raw
    // JsonException escaped the Redis-only resilience decorator (JsonException is not a
    // RedisException) and surfaced as a spurious 500 on the auth path — with no self-heal
    // until the key's TTL (up to the 30-day long-lifetime profile). null → the auth handler
    // 401s → the holder re-logins → a fresh valid session is minted → self-heal.

    [Fact]
    public async Task GetAsync_ShouldReturnNull_WhenStoredPayloadIsCorrupt()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = await _store.CreateAsync(Guid.NewGuid(), SessionLifetime.Legacy, ct);
        await PlantCorruptPayloadAsync(session.Id, ct);

        (await _store.GetAsync(session.Id, ct)).ShouldBeNull();
    }

    [Fact]
    public async Task RotateAsync_ShouldReturnNull_WhenStoredPayloadIsCorrupt()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = await _store.CreateAsync(Guid.NewGuid(), SessionLifetime.Legacy, ct);
        await PlantCorruptPayloadAsync(session.Id, ct);

        (await _store.RotateAsync(session.Id, ct)).ShouldBeNull();
    }

    [Fact]
    public async Task InvalidateAsync_ShouldDropMainKeyAndReturnTrue_WhenStoredPayloadIsCorrupt()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var session = await _store.CreateAsync(userId, SessionLifetime.Legacy, ct);
        await PlantCorruptPayloadAsync(session.Id, ct);

        var removed = await _store.InvalidateAsync(session.Id, ct);

        // The main key is dropped and the call reports success even though the payload is
        // unreadable — because the user cannot present a valid session either way.
        removed.ShouldBeTrue();
        (await _db.KeyExistsAsync(RedisKey(session.Id))).ShouldBeFalse();
        // The index SREM is skipped (the payload can't tell us which user's set to touch);
        // the orphan member lingers benignly (a later slide/create no-ops on it).
        (await _db.SetContainsAsync(UserSessionsKey(userId), SessionMember(session.Id))).ShouldBeTrue();
    }

    [Fact]
    public async Task GetAsync_ShouldReturnSession_WhenStoredPayloadIsValid()
    {
        // Happy-path regression: the corrupt-payload guard must NOT swallow a valid payload
        // (pins the deserialize guard against an over-broad "always null" mutation).
        var ct = TestContext.Current.CancellationToken;
        var session = await _store.CreateAsync(Guid.NewGuid(), SessionLifetime.Legacy, ct);

        var loaded = await _store.GetAsync(session.Id, ct);

        loaded.ShouldNotBeNull();
        loaded.UserId.ShouldBe(session.UserId);
    }

    // Overwrites a live session's main key with an unparseable payload, simulating Redis
    // data corruption / a truncated write. Written through the SAME IDistributedCache the
    // store reads (which adds the jobbliggaren: instance prefix) at the store's logical key.
    private Task PlantCorruptPayloadAsync(SessionId sessionId, CancellationToken ct) =>
        _cache.SetStringAsync(
            SessionMember(sessionId),
            "{\"userId\":\"broken", // unterminated JSON string → JsonException on deserialize
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) },
            ct);

    // Unprefixed session member (what RedisSessionStore stores in the user-index SET and
    // passes to IDistributedCache, which adds the jobbliggaren: instance prefix itself).
    private static string SessionMember(SessionId sessionId)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(sessionId.Reveal()), hash);
        return $"session:{Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_')}";
    }

    // Secondary user-sessions-index key as built by RedisSessionStore.UserSessionsKey
    // (manually jobbliggaren:-prefixed since the store uses IConnectionMultiplexer directly).
    private static string UserSessionsKey(Guid userId) =>
        $"jobbliggaren:user:{userId}:sessions";

    // Full Redis key as stored by RedisSessionStore + the IDistributedCache instance prefix.
    private static string RedisKey(SessionId sessionId) => $"jobbliggaren:{SessionMember(sessionId)}";
}
