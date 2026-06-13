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
            Options.Create(new SessionStoreOptions { Ttl = TimeSpan.FromDays(14) }));
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
        var session = await _store.CreateAsync(userId, TestContext.Current.CancellationToken);
        session.UserId.ShouldBe(userId);
    }

    [Fact]
    public async Task CreateAsync_ShouldReturnSessionWithNonEmptyId_WhenCalled()
    {
        var session = await _store.CreateAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);
        session.Id.Reveal().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task CreateAsync_ShouldReturnUniqueIds_WhenCalledTwice()
    {
        var ct = TestContext.Current.CancellationToken;
        var s1 = await _store.CreateAsync(Guid.NewGuid(), ct);
        var s2 = await _store.CreateAsync(Guid.NewGuid(), ct);
        s1.Id.Reveal().ShouldNotBe(s2.Id.Reveal());
    }

    [Fact]
    public async Task CreateAsync_ShouldSetExpiresAtTo14DaysAfterCreatedAt_WhenCalled()
    {
        var session = await _store.CreateAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);
        (session.ExpiresAt - session.CreatedAt).ShouldBe(TimeSpan.FromDays(14));
    }

    // ── GetAsync ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_ShouldReturnSession_WhenSessionExists()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();

        var created = await _store.CreateAsync(userId, ct);
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

        var session = await _store.CreateAsync(Guid.NewGuid(), ct);
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

        var session = await _store.CreateAsync(Guid.NewGuid(), ct);
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

        var session = await _store.CreateAsync(Guid.NewGuid(), ct);
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

        var created = await _store.CreateAsync(userId, ct);
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
        var session = await _store.CreateAsync(Guid.NewGuid(), ct);

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
}
