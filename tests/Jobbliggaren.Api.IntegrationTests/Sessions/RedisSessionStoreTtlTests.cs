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
    private const int MediumTtlSeconds = 5;

    private readonly RedisContainer _redis = new RedisBuilder("redis:8-alpine").Build();

    private RedisSessionStore _shortTtlStore = null!;
    private RedisSessionStore _mediumTtlStore = null!;
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

        _mediumTtlStore = new RedisSessionStore(
            cache,
            _mux,
            _time,
            Options.Create(new SessionStoreOptions { Ttl = TimeSpan.FromSeconds(MediumTtlSeconds) }));

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

    // #502 regression: GetAsync must slide the secondary user-sessions-index SET,
    // not only the main session key. Without this the SET dies _ttl after the LAST
    // login (CreateAsync sets its TTL once) while the main key slides forever →
    // InvalidateAllForUserAsync iterates an empty SET and the token survives account
    // deletion (ADR 0024 D4 / Art. 17). Mirrors GetAsync_ShouldResetRedisTtl above,
    // but asserts the SET key's TTL rather than the main key's.
    [Fact]
    public async Task GetAsync_ShouldRefreshSecondaryIndexSetTtl_WhenSessionExists()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        // Medium TTL (5s) rather than short (3s) so the "before" measurement keeps a
        // ~2s wall-clock margin — the SET must still be alive when ttlBeforeGet is read.
        var session = await _mediumTtlStore.CreateAsync(userId, ct);
        var setKey = UserSessionsKey(userId);

        // Wait past half the TTL so the remaining SET TTL is visibly shorter
        await Task.Delay(TimeSpan.FromSeconds(MediumTtlSeconds / 2.0 + 0.5), ct);

        var ttlBeforeGet = await _db.KeyTimeToLiveAsync(setKey);

        await _mediumTtlStore.GetAsync(session.Id, ct);

        var ttlAfterGet = await _db.KeyTimeToLiveAsync(setKey);

        ttlBeforeGet.ShouldNotBeNull();
        ttlAfterGet.ShouldNotBeNull();
        ttlAfterGet!.Value.ShouldBeGreaterThan(ttlBeforeGet!.Value);
    }

    // #502 pins the re-SADD half of the fix (not just KeyExpire): if the secondary
    // index SET was evicted/expired while the main session key still lives (the exact
    // #502 orphan state), a read must RECREATE the membership. A bare KeyExpire on a
    // gone key is a no-op — without SetAddAsync this stays broken.
    [Fact]
    public async Task GetAsync_ShouldRecreateSecondaryIndexMembership_WhenSetWasEvicted()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var session = await _defaultStore.CreateAsync(userId, ct);
        var setKey = UserSessionsKey(userId);

        // Simulate the orphan state directly: index gone, main key alive. No wall-clock
        // dependency — deterministic.
        await _db.KeyDeleteAsync(setKey);
        (await _db.KeyExistsAsync(setKey)).ShouldBeFalse();

        (await _defaultStore.GetAsync(session.Id, ct)).ShouldNotBeNull();

        (await _db.KeyExistsAsync(setKey)).ShouldBeTrue();
        (await _defaultStore.InvalidateAllForUserAsync(userId, ct)).ShouldBe(1);
    }

    // #502 negative case: a read of an already-expired session must NOT resurrect the
    // secondary index. The null-check returns before the re-SADD block; re-adding a
    // dead session's membership would break natural cleanup and the "index tracks only
    // live sessions" invariant.
    [Fact]
    public async Task GetAsync_ShouldNotRecreateSecondaryIndex_WhenSessionExpired()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var session = await _shortTtlStore.CreateAsync(userId, ct);
        var setKey = UserSessionsKey(userId);

        // Let both the main key and the SET expire (delay strictly exceeds the TTL).
        await Task.Delay(TimeSpan.FromSeconds(ShortTtlSeconds + 1), ct);

        (await _shortTtlStore.GetAsync(session.Id, ct)).ShouldBeNull();

        (await _db.KeyExistsAsync(setKey)).ShouldBeFalse();
    }

    // #502 behavioural end-to-end: reads that slide the session past the ORIGINAL
    // set-TTL must keep the secondary index alive, so a later DELETE /me
    // (InvalidateAllForUserAsync) still revokes the still-authenticating session.
    [Fact]
    public async Task InvalidateAllForUserAsync_ShouldStillRevoke_WhenReadsSlidePastOriginalSetTtl()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var session = await _mediumTtlStore.CreateAsync(userId, ct);
        var setKey = UserSessionsKey(userId);

        // Two reads at ~0.6×TTL each: the second read lands PAST the original
        // set-TTL, so the SET only survives if GetAsync refreshed it.
        await Task.Delay(TimeSpan.FromSeconds(MediumTtlSeconds * 0.6), ct);
        (await _mediumTtlStore.GetAsync(session.Id, ct)).ShouldNotBeNull();
        await Task.Delay(TimeSpan.FromSeconds(MediumTtlSeconds * 0.6), ct);
        (await _mediumTtlStore.GetAsync(session.Id, ct)).ShouldNotBeNull();

        // The still-live session must still be tracked by the secondary index.
        (await _db.KeyExistsAsync(setKey)).ShouldBeTrue();

        var revoked = await _mediumTtlStore.InvalidateAllForUserAsync(userId, ct);

        revoked.ShouldBe(1);
        (await _mediumTtlStore.GetAsync(session.Id, ct)).ShouldBeNull();
    }

    // #502 multi-session invariant: the SET-TTL must track max(last-read)+ttl across
    // all of a user's sessions, so InvalidateAllForUserAsync revokes every one even
    // after the original set-TTL would have elapsed.
    [Fact]
    public async Task InvalidateAllForUserAsync_ShouldRevokeAllSessions_WhenReadsSlidePastOriginalSetTtl()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var sessionA = await _mediumTtlStore.CreateAsync(userId, ct);
        var sessionB = await _mediumTtlStore.CreateAsync(userId, ct);

        await Task.Delay(TimeSpan.FromSeconds(MediumTtlSeconds * 0.6), ct);
        (await _mediumTtlStore.GetAsync(sessionA.Id, ct)).ShouldNotBeNull();
        (await _mediumTtlStore.GetAsync(sessionB.Id, ct)).ShouldNotBeNull();
        await Task.Delay(TimeSpan.FromSeconds(MediumTtlSeconds * 0.6), ct);
        (await _mediumTtlStore.GetAsync(sessionA.Id, ct)).ShouldNotBeNull();
        (await _mediumTtlStore.GetAsync(sessionB.Id, ct)).ShouldNotBeNull();

        var revoked = await _mediumTtlStore.InvalidateAllForUserAsync(userId, ct);

        revoked.ShouldBe(2);
        (await _mediumTtlStore.GetAsync(sessionA.Id, ct)).ShouldBeNull();
        (await _mediumTtlStore.GetAsync(sessionB.Id, ct)).ShouldBeNull();
    }

    // Secondary user-sessions-index key as built by RedisSessionStore.UserSessionsKey
    // (manually jobbliggaren:-prefixed since the store uses IConnectionMultiplexer directly).
    private static string UserSessionsKey(Guid userId) =>
        $"jobbliggaren:user:{userId}:sessions";

    // Computes the full Redis key as stored by RedisSessionStore + IDistributedCache prefix
    private static string RedisKey(SessionId sessionId)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(sessionId.Reveal()), hash);
        var hashed = Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"jobbliggaren:session:{hashed}";
    }
}
