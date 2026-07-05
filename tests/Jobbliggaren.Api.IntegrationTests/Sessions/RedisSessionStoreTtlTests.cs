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

    // #2b3 COND-A: rotation mints a new id, preserves CreatedAt (the cap anchor), and instead
    // of hard-deleting the old key retires it into a bounded, non-sliding GRACE window — so a
    // concurrent in-flight request still bearing the old id authenticates briefly rather than
    // 401-ing. The superseded old key stays in the user index, so InvalidateAllForUser still
    // revokes it (Art. 17) alongside the new session.
    [Fact]
    public async Task RotateAsync_ShouldSupersedeOldIntoGraceAndKeepIndex_WhenDue()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var session = await _rotatingStore.CreateAsync(userId, SessionLifetime.Persistent, ct);

        _capClock.UtcNow = _capClock.UtcNow.AddHours(25); // past the 24h rotation interval

        var rotation = await _rotatingStore.RotateAsync(session.Id, ct);

        rotation.ShouldNotBeNull();
        rotation!.NewId.Reveal().ShouldNotBe(session.Id.Reveal());

        // Old key NOT hard-deleted: it survives on a short, fixed grace TTL (~60s), not the
        // 30d sliding window — proving the grace is bounded and non-sliding.
        (await _db.KeyExistsAsync(RedisKey(session.Id))).ShouldBeTrue();
        var oldTtl = await _db.KeyTimeToLiveAsync(RedisKey(session.Id));
        oldTtl.ShouldNotBeNull();
        oldTtl!.Value.ShouldBeLessThan(TimeSpan.FromMinutes(5));

        // The old id still authenticates within grace, and a read does NOT slide its TTL.
        (await _rotatingStore.GetAsync(session.Id, ct)).ShouldNotBeNull();
        var oldTtlAfterRead = await _db.KeyTimeToLiveAsync(RedisKey(session.Id));
        oldTtlAfterRead!.Value.ShouldBeLessThan(TimeSpan.FromMinutes(5));

        // New session valid with CreatedAt carried verbatim.
        var rotated = await _rotatingStore.GetAsync(rotation.NewId, ct);
        rotated.ShouldNotBeNull();
        rotated!.CreatedAt.ShouldBe(session.CreatedAt);

        // Index tracks BOTH the new session and the superseded old key, so bulk-invalidate
        // (Art. 17) revokes both — the grace-window old id cannot outlive account deletion.
        (await _rotatingStore.InvalidateAllForUserAsync(userId, ct)).ShouldBe(2);
        (await _rotatingStore.GetAsync(rotation.NewId, ct)).ShouldBeNull();
        (await _rotatingStore.GetAsync(session.Id, ct)).ShouldBeNull();
    }

    // PR2c-0 Layer 2 (security-auditor gate): the :deleted tombstone gate runs BEFORE the COND-A
    // grace short-circuit, so a rotated-away (superseded) key still in its grace window is ALSO
    // rejected once the user is tombstoned — deletion overrides an in-flight rotation grace (both
    // ids die). Mirrors the InMemory placement-proof against REAL Redis on the hot auth path.
    [Fact]
    public async Task GetAsync_ShouldReturnNull_ForSupersededAndNewKey_WhenUserMarkedDeleted()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var session = await _rotatingStore.CreateAsync(userId, SessionLifetime.Persistent, ct);

        _capClock.UtcNow = _capClock.UtcNow.AddHours(25); // past the 24h rotation interval
        var rotation = await _rotatingStore.RotateAsync(session.Id, ct);
        rotation.ShouldNotBeNull();

        // Both valid immediately after rotation (old in COND-A grace, new live)…
        (await _rotatingStore.GetAsync(session.Id, ct)).ShouldNotBeNull();
        (await _rotatingStore.GetAsync(rotation!.NewId, ct)).ShouldNotBeNull();

        await _rotatingStore.MarkUserDeletedAsync(userId, ct);

        // …and both die the moment the user is tombstoned (the gate precedes the grace check).
        (await _rotatingStore.GetAsync(session.Id, ct)).ShouldBeNull();
        (await _rotatingStore.GetAsync(rotation.NewId, ct)).ShouldBeNull();
    }

    // #2b3 COND-B: a rotation that runs concurrently with an account-wide revoke must fail
    // closed rather than mint a session that outlives the deletion. InvalidateAllForUser plants
    // a per-user tombstone before snapshotting the index; RotateAsync checks it after writing
    // the new key. Seeding the tombstone directly proves the fail-closed branch: no new session,
    // old id gone.
    [Fact]
    public async Task RotateAsync_ShouldFailClosed_WhenRevocationTombstonePresent()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var session = await _rotatingStore.CreateAsync(userId, SessionLifetime.Persistent, ct);

        _capClock.UtcNow = _capClock.UtcNow.AddHours(25);

        // Simulate a concurrent InvalidateAllForUserAsync having planted the tombstone.
        await _db.StringSetAsync($"jobbliggaren:user:{userId}:revoked", "1", TimeSpan.FromSeconds(60));

        var rotation = await _rotatingStore.RotateAsync(session.Id, ct);

        rotation.ShouldBeNull(); // fail closed
        // Neither the old nor any new session may survive the revoke.
        (await _rotatingStore.GetAsync(session.Id, ct)).ShouldBeNull();
        (await _rotatingStore.InvalidateAllForUserAsync(userId, ct)).ShouldBe(0);
    }

    // #2b3 COND-B regression: the fail-closed guard must hold for the REAL interleave (a revoke
    // landing DURING a rotation), not only when the tombstone is pre-seeded. Race RotateAsync
    // against InvalidateAllForUserAsync against real Redis, many times; the invariant is that
    // once both complete, NO session id — old or new — still authenticates, whichever way Redis
    // serialized the two command streams. Pre-fix (superseded-old written AFTER the tombstone
    // check) a sweep could resurrect the old id outside the index; this pins that closed. Post-
    // fix every rotation write precedes the single tombstone check, so the property is by
    // construction and this never flakes.
    [Fact]
    public async Task RotateAsync_ShouldLeaveNoSurvivingSession_WhenRacingConcurrentInvalidateAll()
    {
        var ct = TestContext.Current.CancellationToken;

        for (var i = 0; i < 30; i++)
        {
            var userId = Guid.NewGuid();
            var session = await _rotatingStore.CreateAsync(userId, SessionLifetime.Persistent, ct);
            _capClock.UtcNow = _capClock.UtcNow.AddHours(25); // past the 24h rotation interval

            var rotateTask = _rotatingStore.RotateAsync(session.Id, ct);
            var invalidateTask = _rotatingStore.InvalidateAllForUserAsync(userId, ct);
            await Task.WhenAll(rotateTask, invalidateTask);

            var rotation = await rotateTask;
            (await _rotatingStore.GetAsync(session.Id, ct)).ShouldBeNull();
            if (rotation is not null)
                (await _rotatingStore.GetAsync(rotation.NewId, ct)).ShouldBeNull();
        }
    }

    // #2b3 back-compat: a 2a/2b-1-era payload has Lifetime + RotatedAt but no SupersededAt.
    // It must deserialize with SupersededAt = null (a live, non-superseded session) so no
    // session breaks on the deploy that introduces the grace field.
    [Fact]
    public async Task GetAsync_ShouldTreatPayloadWithoutSupersededAtAsLive_WhenFieldAbsent()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var sessionId = SessionId.Generate();
        var member = SessionMember(sessionId);

        var createdAt = _capClock.UtcNow;
        // Pre-2b3 payload: lifetime + rotatedAt present, supersededAt absent.
        var json = $"{{\"userId\":\"{userId}\",\"createdAt\":\"{createdAt:O}\",\"rotatedAt\":\"{createdAt:O}\",\"lifetime\":2}}";
        await _cache.SetStringAsync(
            member, json, new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromDays(30) }, ct);
        await _db.SetAddAsync(UserSessionsKey(userId), member);

        // Read as a normal live session (slid), not treated as superseded.
        var fetched = await _rotatingStore.GetAsync(sessionId, ct);
        fetched.ShouldNotBeNull();
        fetched!.UserId.ShouldBe(userId);
        // A live session slides to a full window; a superseded one would pin to a ~60s ceiling.
        (fetched.ExpiresAt - _capClock.UtcNow).ShouldBeGreaterThan(TimeSpan.FromDays(1));
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
