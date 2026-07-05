using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Auth.Sessions;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Options;
using Shouldly;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Jobbliggaren.Api.IntegrationTests.Sessions;

public class RedisSessionStoreTests : IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder("redis:8-alpine").Build();
    private readonly ITestOutputHelper _output;

    private RedisSessionStore _store = null!;
    private FakeDateTimeProvider _time = null!;
    private ConnectionMultiplexer _mux = null!;

    public RedisSessionStoreTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async ValueTask InitializeAsync()
    {
        await _redis.StartAsync();

        var cache = new RedisCache(Options.Create(
            new RedisCacheOptions
            {
                Configuration = _redis.GetConnectionString(),
                InstanceName = "jobbliggaren:",
            }));

        _mux = (ConnectionMultiplexer)await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString());
        _time = FakeDateTimeProvider.Now;
        _store = new RedisSessionStore(
            cache,
            _mux,
            _time,
            Options.Create(new SessionStoreOptions { Legacy = new SessionLifetimeProfile { SlidingTtl = TimeSpan.FromDays(14) } }));
    }

    public async ValueTask DisposeAsync()
    {
        await _mux.CloseAsync();
        _mux.Dispose();
        await _redis.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    // ── CreateAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ShouldReturnSessionWithMatchingUserId_WhenCalled()
    {
        var userId = Guid.NewGuid();
        var session = await _store.CreateAsync(userId, SessionLifetime.Legacy, TestContext.Current.CancellationToken);
        session.UserId.ShouldBe(userId);
    }

    [Fact]
    public async Task CreateAsync_ShouldReturnSessionWithNonEmptyId_WhenCalled()
    {
        var session = await _store.CreateAsync(Guid.NewGuid(), SessionLifetime.Legacy, TestContext.Current.CancellationToken);
        session.Id.Reveal().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task CreateAsync_ShouldReturnUniqueIds_WhenCalledTwice()
    {
        var ct = TestContext.Current.CancellationToken;
        var s1 = await _store.CreateAsync(Guid.NewGuid(), SessionLifetime.Legacy, ct);
        var s2 = await _store.CreateAsync(Guid.NewGuid(), SessionLifetime.Legacy, ct);
        s1.Id.Reveal().ShouldNotBe(s2.Id.Reveal());
    }

    [Fact]
    public async Task CreateAsync_ShouldSetExpiresAtTo14DaysAfterCreatedAt_WhenCalled()
    {
        var session = await _store.CreateAsync(Guid.NewGuid(), SessionLifetime.Legacy, TestContext.Current.CancellationToken);
        (session.ExpiresAt - session.CreatedAt).ShouldBe(TimeSpan.FromDays(14));
    }

    // ── GetAsync ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_ShouldReturnSession_WhenSessionExists()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();

        var created = await _store.CreateAsync(userId, SessionLifetime.Legacy, ct);
        var fetched = await _store.GetAsync(created.Id, ct);

        fetched.ShouldNotBeNull();
        fetched!.Id.Reveal().ShouldBe(created.Id.Reveal());
        fetched.UserId.ShouldBe(userId);
    }

    [Fact]
    public async Task GetAsync_ShouldReturnNull_WhenSessionDoesNotExist()
    {
        var result = await _store.GetAsync(
            SessionId.FromRaw("nonexistent-session-id-xxx"),
            TestContext.Current.CancellationToken);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetAsync_ShouldReturnNull_WhenSessionWasInvalidated()
    {
        var ct = TestContext.Current.CancellationToken;

        var session = await _store.CreateAsync(Guid.NewGuid(), SessionLifetime.Legacy, ct);
        await _store.InvalidateAsync(session.Id, ct);
        var result = await _store.GetAsync(session.Id, ct);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetAsync_ShouldReturnNull_WhenInputIsEmptyString()
    {
        var result = await _store.GetAsync(
            SessionId.FromRaw(string.Empty),
            TestContext.Current.CancellationToken);

        result.ShouldBeNull();
    }

    // ── InvalidateAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task InvalidateAsync_ShouldReturnTrue_WhenSessionExists()
    {
        var ct = TestContext.Current.CancellationToken;

        var session = await _store.CreateAsync(Guid.NewGuid(), SessionLifetime.Legacy, ct);
        var result = await _store.InvalidateAsync(session.Id, ct);

        result.ShouldBeTrue();
    }

    [Fact]
    public async Task InvalidateAsync_ShouldReturnFalse_WhenSessionDoesNotExist()
    {
        var result = await _store.InvalidateAsync(
            SessionId.FromRaw("never-existed"),
            TestContext.Current.CancellationToken);

        result.ShouldBeFalse();
    }

    [Fact]
    public async Task InvalidateAsync_ShouldReturnFalse_WhenSessionAlreadyInvalidated()
    {
        var ct = TestContext.Current.CancellationToken;

        var session = await _store.CreateAsync(Guid.NewGuid(), SessionLifetime.Legacy, ct);
        await _store.InvalidateAsync(session.Id, ct);
        var result = await _store.InvalidateAsync(session.Id, ct);

        result.ShouldBeFalse();
    }

    [Fact]
    public async Task InvalidateAsync_ShouldReturnFalse_WhenInputIsEmptyString()
    {
        var result = await _store.InvalidateAsync(
            SessionId.FromRaw(string.Empty),
            TestContext.Current.CancellationToken);

        result.ShouldBeFalse();
    }

    // ── Round-trip ────────────────────────────────────────────────────────────

    [Fact]
    public async Task RoundTrip_ShouldPreserveUserIdAndCreatedAt_WhenSerializedAndDeserialized()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();

        var created = await _store.CreateAsync(userId, SessionLifetime.Legacy, ct);
        var fetched = await _store.GetAsync(created.Id, ct);

        fetched.ShouldNotBeNull();
        fetched!.UserId.ShouldBe(userId);
        fetched.CreatedAt.ShouldBe(created.CreatedAt);
    }

    // ── Performance ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_Performance_1000Calls_P99Under5ms()
    {
        const int Iterations = 1000;
        var ct = TestContext.Current.CancellationToken;

        // Pre-create session for lookup
        var session = await _store.CreateAsync(Guid.NewGuid(), SessionLifetime.Legacy, ct);

        // Warmup
        for (var i = 0; i < 10; i++)
            await _store.GetAsync(session.Id, ct);

        var timings = new double[Iterations];
        var sw = System.Diagnostics.Stopwatch.StartNew();

        for (var i = 0; i < Iterations; i++)
        {
            var t = System.Diagnostics.Stopwatch.GetTimestamp();
            await _store.GetAsync(session.Id, ct);
            timings[i] = (System.Diagnostics.Stopwatch.GetTimestamp() - t)
                         / (double)System.Diagnostics.Stopwatch.Frequency * 1000.0;
        }

        sw.Stop();

        Array.Sort(timings);
        var min = timings[0];
        var p50 = timings[(int)(Iterations * 0.50)];
        var p99 = timings[(int)(Iterations * 0.99)];
        var max = timings[Iterations - 1];

        _output.WriteLine(
            $"[PERF] ISessionStore.GetAsync — min: {min:F2} ms, p50: {p50:F2} ms, p99: {p99:F2} ms, max: {max:F2} ms");

        p99.ShouldBeLessThan(50.0,
            "p99 > 50 ms mot lokal Docker Redis är oacceptabelt (budget är 5 ms mot prod Redis)");
    }

    // ── MarkUserDeletedAsync (PR2c-0 Layer 2 soft-delete gate) ─────────────────

    // Core Layer-2 property against real Redis: a tombstoned user's surviving session fails
    // closed on read — the read-path erasure backstop for a session that outlived the
    // best-effort InvalidateAllForUserAsync.
    [Fact]
    public async Task GetAsync_ShouldReturnNull_WhenUserMarkedDeleted()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();

        var session = await _store.CreateAsync(userId, SessionLifetime.Legacy, ct);
        (await _store.GetAsync(session.Id, ct)).ShouldNotBeNull();

        await _store.MarkUserDeletedAsync(userId, ct);

        (await _store.GetAsync(session.Id, ct)).ShouldBeNull();
    }

    // Per-user: deleting one user must not reject another user's sessions.
    [Fact]
    public async Task GetAsync_ShouldNotAffectOtherUsers_WhenUserMarkedDeleted()
    {
        var ct = TestContext.Current.CancellationToken;
        var deletedUser = Guid.NewGuid();
        var otherUser = Guid.NewGuid();

        var deletedSession = await _store.CreateAsync(deletedUser, SessionLifetime.Legacy, ct);
        var otherSession = await _store.CreateAsync(otherUser, SessionLifetime.Legacy, ct);

        await _store.MarkUserDeletedAsync(deletedUser, ct);

        (await _store.GetAsync(deletedSession.Id, ct)).ShouldBeNull();
        (await _store.GetAsync(otherSession.Id, ct)).ShouldNotBeNull();
    }

    // Self-heal proof: the rejected read EVICTS the session key (not merely masks it). After the
    // tombstone is manually cleared, the session is STILL gone — so a Redis blip that later drops
    // the tombstone cannot resurrect the session. Mirrors the absolute-cap eviction.
    [Fact]
    public async Task GetAsync_ShouldEvictSession_WhenUserMarkedDeleted()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();

        var session = await _store.CreateAsync(userId, SessionLifetime.Legacy, ct);
        await _store.MarkUserDeletedAsync(userId, ct);
        (await _store.GetAsync(session.Id, ct)).ShouldBeNull(); // evicts the session key

        // Drop the tombstone directly; the session must NOT reappear (it was evicted, not masked).
        await _mux.GetDatabase().KeyDeleteAsync($"jobbliggaren:user:{userId}:deleted");
        (await _store.GetAsync(session.Id, ct)).ShouldBeNull();
    }

    // The tombstone carries the 30-day restore-window TTL (DeletionTombstoneTtl), so it
    // self-expires when hard-delete makes the id moot — it never blocks a later account forever.
    [Fact]
    public async Task MarkUserDeletedAsync_ShouldSetTombstoneWithRestoreWindowTtl()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();

        await _store.MarkUserDeletedAsync(userId, ct);

        var ttl = await _mux.GetDatabase().KeyTimeToLiveAsync($"jobbliggaren:user:{userId}:deleted");
        ttl.ShouldNotBeNull();
        ttl!.Value.ShouldBeGreaterThan(TimeSpan.FromDays(29));
        ttl.Value.ShouldBeLessThanOrEqualTo(TimeSpan.FromDays(30));
    }
}
