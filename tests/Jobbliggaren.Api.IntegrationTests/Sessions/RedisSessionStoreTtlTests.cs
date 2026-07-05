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

public class RedisSessionStoreTtlTests : IAsyncLifetime
{
    private const int ShortTtlSeconds = 3;
    private const int MediumTtlSeconds = 5;

    private readonly RedisContainer _redis = new RedisBuilder("redis:8-alpine").Build();

    private RedisSessionStore _shortTtlStore = null!;
    private RedisSessionStore _mediumTtlStore = null!;
    private RedisSessionStore _defaultStore = null!;
    private RedisSessionStore _cappedStore = null!;
    private RedisSessionStore _rotatingStore = null!;
    private IDatabase _db = null!;
    private ConnectionMultiplexer _mux = null!;
    private RedisCache _cache = null!;
    private FakeDateTimeProvider _time = null!;
    // Time-travelable clock driving the absolute-cap test (the real Redis key TTL is
    // 14d wall-clock so the key survives the sub-second run while the fake clock jumps).
    private readonly MutableFakeDateTimeProvider _capClock = new();

    public async ValueTask InitializeAsync()
    {
        await _redis.StartAsync();

        var cs = _redis.GetConnectionString();
        _mux = (ConnectionMultiplexer)await ConnectionMultiplexer.ConnectAsync(cs);
        _db = _mux.GetDatabase();

        _cache = new RedisCache(Options.Create(
            new RedisCacheOptions { Configuration = cs, InstanceName = "jobbliggaren:" }));
        var cache = _cache;

        _time = FakeDateTimeProvider.Now;

        _shortTtlStore = new RedisSessionStore(
            cache,
            _mux,
            _time,
            Options.Create(new SessionStoreOptions { Legacy = new SessionLifetimeProfile { SlidingTtl = TimeSpan.FromSeconds(ShortTtlSeconds) } }));

        _mediumTtlStore = new RedisSessionStore(
            cache,
            _mux,
            _time,
            Options.Create(new SessionStoreOptions { Legacy = new SessionLifetimeProfile { SlidingTtl = TimeSpan.FromSeconds(MediumTtlSeconds) } }));

        _defaultStore = new RedisSessionStore(
            cache,
            _mux,
            _time,
            Options.Create(new SessionStoreOptions { Legacy = new SessionLifetimeProfile { SlidingTtl = TimeSpan.FromDays(14) } }));

        // 14d sliding + 30d absolute cap, driven by the mutable clock so the cap test
        // can jump time. Real Redis key TTL stays 14d (wall-clock) → key alive for the run.
        _cappedStore = new RedisSessionStore(
            cache,
            _mux,
            _capClock,
            Options.Create(new SessionStoreOptions
            {
                Legacy = new SessionLifetimeProfile
                {
                    SlidingTtl = TimeSpan.FromDays(14),
                    AbsoluteTtl = TimeSpan.FromDays(30),
                },
            }));

        // Default profiles (Persistent 30d/30d + 24h rotation), driven by the mutable
        // clock so the rotation tests can jump past the interval.
        _rotatingStore = new RedisSessionStore(cache, _mux, _capClock, Options.Create(new SessionStoreOptions()));
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
        var session = await _defaultStore.CreateAsync(Guid.NewGuid(), SessionLifetime.Legacy, ct);

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
        var session = await _shortTtlStore.CreateAsync(Guid.NewGuid(), SessionLifetime.Legacy, ct);

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
        var session = await _shortTtlStore.CreateAsync(Guid.NewGuid(), SessionLifetime.Legacy, ct);

        await Task.Delay(TimeSpan.FromSeconds(ShortTtlSeconds + 1), ct);

        var result = await _shortTtlStore.GetAsync(session.Id, ct);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task InvalidateAsync_ShouldRemoveKeyImmediately_WhenCalled()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = await _defaultStore.CreateAsync(Guid.NewGuid(), SessionLifetime.Legacy, ct);

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
        var session = await _mediumTtlStore.CreateAsync(userId, SessionLifetime.Legacy, ct);
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
        var session = await _defaultStore.CreateAsync(userId, SessionLifetime.Legacy, ct);
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
        var session = await _shortTtlStore.CreateAsync(userId, SessionLifetime.Legacy, ct);
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
        var session = await _mediumTtlStore.CreateAsync(userId, SessionLifetime.Legacy, ct);
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
        var sessionA = await _mediumTtlStore.CreateAsync(userId, SessionLifetime.Legacy, ct);
        var sessionB = await _mediumTtlStore.CreateAsync(userId, SessionLifetime.Legacy, ct);

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

    // #481 Low / #620: the absolute cap must end a session at CreatedAt + AbsoluteTtl
    // even when it is actively read. Reads at +10d and +20d slide it (each within the
    // 14d window); the read at +31d crosses the 30d cap → null, and the session is
    // evicted from BOTH the main key and the user index (so InvalidateAllForUser → 0).
    [Fact]
    public async Task GetAsync_ShouldReturnNullAndEvict_WhenAbsoluteCapExceeded_DespiteActiveReads()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var session = await _cappedStore.CreateAsync(userId, SessionLifetime.Legacy, ct);

        _capClock.UtcNow = _capClock.UtcNow.AddDays(10);
        (await _cappedStore.GetAsync(session.Id, ct)).ShouldNotBeNull();
        _capClock.UtcNow = _capClock.UtcNow.AddDays(10); // +20d, read 10d ago → still valid
        (await _cappedStore.GetAsync(session.Id, ct)).ShouldNotBeNull();

        _capClock.UtcNow = _capClock.UtcNow.AddDays(11); // +31d → past the 30d absolute cap
        (await _cappedStore.GetAsync(session.Id, ct)).ShouldBeNull();

        (await _db.KeyExistsAsync(RedisKey(session.Id))).ShouldBeFalse();
        (await _cappedStore.InvalidateAllForUserAsync(userId, ct)).ShouldBe(0);
    }

    // The clamp: a read near the ceiling sets the Redis key TTL to the remaining cap
    // window, not a full 14d — so the key never outlives CreatedAt + AbsoluteTtl.
    [Fact]
    public async Task GetAsync_ShouldClampRedisTtlToRemainingCap_WhenNearCeiling()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = await _cappedStore.CreateAsync(Guid.NewGuid(), SessionLifetime.Legacy, ct);

        _capClock.UtcNow = _capClock.UtcNow.AddDays(29); // 1 day of cap remains (< 14d sliding)
        (await _cappedStore.GetAsync(session.Id, ct)).ShouldNotBeNull();

        var ttl = await _db.KeyTimeToLiveAsync(RedisKey(session.Id));
        ttl.ShouldNotBeNull();
        // ~1 day, not ~14 days: clamped to the remaining absolute window.
        ttl!.Value.ShouldBeLessThan(TimeSpan.FromDays(2));
    }

    // #626 back-compat: a pre-profiles payload has only userId + createdAt (no Lifetime
    // / RotatedAt). It must deserialize to Legacy (ordinal 0) and honour today's 30d cap
    // — so nobody is logged out on the deploy that introduces lifetime profiles. Seeds
    // the legacy JSON directly through the cache (RedisCache hash format) so GetAsync
    // reads it exactly as it would a real pre-2a session.
    [Fact]
    public async Task GetAsync_ShouldTreatLegacyPayloadAsLegacyProfile_WhenLifetimeFieldAbsent()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var sessionId = SessionId.Generate();
        var member = SessionMember(sessionId);
        var setKey = UserSessionsKey(userId);

        var createdAt = _capClock.UtcNow;
        var legacyJson = $"{{\"userId\":\"{userId}\",\"createdAt\":\"{createdAt:O}\"}}";
        await _cache.SetStringAsync(
            member,
            legacyJson,
            new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromDays(14) },
            ct);
        await _db.SetAddAsync(setKey, member);
        await _db.KeyExpireAsync(setKey, TimeSpan.FromDays(14));

        // Missing Lifetime → Legacy → session is found and honours the 30d cap.
        (await _cappedStore.GetAsync(sessionId, ct)).ShouldNotBeNull();

        // Past the 30d Legacy ceiling it is evicted (not silently treated as a longer profile).
        _capClock.UtcNow = _capClock.UtcNow.AddDays(31);
        (await _cappedStore.GetAsync(sessionId, ct)).ShouldBeNull();
    }

    // #2b1: rotation mints a new id, hard-deletes the old key, preserves CreatedAt (the
    // cap anchor), and updates the user index so InvalidateAllForUser still revokes the
    // rotated session.
    [Fact]
    public async Task RotateAsync_ShouldMintNewIdHardDeleteOldAndKeepIndex_WhenDue()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var session = await _rotatingStore.CreateAsync(userId, SessionLifetime.Persistent, ct);

        _capClock.UtcNow = _capClock.UtcNow.AddHours(25); // past the 24h rotation interval

        var rotation = await _rotatingStore.RotateAsync(session.Id, ct);

        rotation.ShouldNotBeNull();
        rotation!.NewId.Reveal().ShouldNotBe(session.Id.Reveal());

        // Old key hard-deleted; new session valid with CreatedAt carried verbatim.
        (await _db.KeyExistsAsync(RedisKey(session.Id))).ShouldBeFalse();
        var rotated = await _rotatingStore.GetAsync(rotation.NewId, ct);
        rotated.ShouldNotBeNull();
        rotated!.CreatedAt.ShouldBe(session.CreatedAt);

        // Index followed the rotation: bulk-invalidate revokes exactly the new session.
        (await _rotatingStore.InvalidateAllForUserAsync(userId, ct)).ShouldBe(1);
        (await _rotatingStore.GetAsync(rotation.NewId, ct)).ShouldBeNull();
    }

    [Fact]
    public async Task RotateAsync_ShouldReturnNull_WhenNotDue()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = await _rotatingStore.CreateAsync(Guid.NewGuid(), SessionLifetime.Persistent, ct);

        _capClock.UtcNow = _capClock.UtcNow.AddHours(1); // within the 24h interval

        (await _rotatingStore.RotateAsync(session.Id, ct)).ShouldBeNull();
        (await _rotatingStore.GetAsync(session.Id, ct)).ShouldNotBeNull();
    }

    [Fact]
    public async Task RotateAsync_ShouldReturnNull_ForNonRotatingProfile()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = await _rotatingStore.CreateAsync(Guid.NewGuid(), SessionLifetime.Legacy, ct);

        _capClock.UtcNow = _capClock.UtcNow.AddDays(5);

        (await _rotatingStore.RotateAsync(session.Id, ct)).ShouldBeNull();
    }

    // True concurrency against real Redis: the SET NX claim elects exactly one rotator.
    [Fact]
    public async Task RotateAsync_ShouldElectSingleWinner_WhenConcurrent()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = await _rotatingStore.CreateAsync(Guid.NewGuid(), SessionLifetime.Persistent, ct);

        _capClock.UtcNow = _capClock.UtcNow.AddHours(25);

        var results = await Task.WhenAll(
            Enumerable.Range(0, 20).Select(_ => _rotatingStore.RotateAsync(session.Id, ct)));

        results.Count(r => r is not null).ShouldBe(1);
    }

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

    // Computes the full Redis key as stored by RedisSessionStore + IDistributedCache prefix
    private static string RedisKey(SessionId sessionId)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(sessionId.Reveal()), hash);
        var hashed = Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"jobbliggaren:session:{hashed}";
    }
}
