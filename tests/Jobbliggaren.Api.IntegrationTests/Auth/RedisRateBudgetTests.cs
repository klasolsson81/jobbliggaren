using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Auth;
using Shouldly;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// The login challenge's per-address counter against a real Redis (ADR 0142 D1). The properties are Redis
/// semantics — atomic increment, the TTL set in the same transaction, NX so a refused call never extends
/// the window — so they are measured here rather than on a fake.
/// </summary>
public sealed class RedisRateBudgetTests : IAsyncLifetime
{
    // The deploy stack's own `redis-volatile`, so the contract is measured on the configuration the box runs.
    private readonly RedisContainer _redis = VolatileRedisContainer.FromDeployCompose();

    // The test's OWN reader, beside the connection the adapter is given: production reaches this instance
    // only through VolatileRedisConnection, and the assertions below read keys and TTLs directly.
    private ConnectionMultiplexer _mux = null!;
    private VolatileRedisConnection _connection = null!;
    private RedisRateBudget _budget = null!;

    public async ValueTask InitializeAsync()
    {
        await _redis.StartAsync();
        var connectionString = $"{VolatileRedisContainer.OperatorConnectionString(_redis)},connectTimeout=1000,syncTimeout=1000";
        _mux = (ConnectionMultiplexer)await ConnectionMultiplexer.ConnectAsync(connectionString);
        _connection = new VolatileRedisConnection(connectionString);
        _budget = new RedisRateBudget(_connection);
    }

    public async ValueTask DisposeAsync()
    {
        _connection.Dispose();
        await _mux.CloseAsync();
        _mux.Dispose();
        await VolatileRedisContainer.DisposeAsync(_redis);
    }

    private static RateBudgetScope Scope(int limit, TimeSpan window, string name = "test-scope") =>
        new(name, limit, window);

    [Fact]
    public async Task Admits_exactly_the_limit_then_refuses()
    {
        var ct = TestContext.Current.CancellationToken;
        var scope = Scope(3, TimeSpan.FromMinutes(10));

        var answers = new List<bool>();
        for (var i = 0; i < 5; i++)
            answers.Add(await _budget.TryConsumeAsync(scope, "a@example.com", ct));

        answers.ShouldBe([true, true, true, false, false]);
    }

    [Fact]
    public async Task The_first_counted_call_sets_the_window_as_the_ttl_and_a_refused_call_does_not_extend_it()
    {
        var ct = TestContext.Current.CancellationToken;
        var scope = Scope(1, TimeSpan.FromMinutes(10));
        var key = RedisRateBudget.Key(scope, "b@example.com");
        var db = _mux.GetDatabase();

        await _budget.TryConsumeAsync(scope, "b@example.com", ct);
        var ttlAfterFirst = await db.KeyTimeToLiveAsync(key);

        // Shorten the TTL by hand: if a later call re-applied EXPIRE, it would restore the full window.
        await db.KeyExpireAsync(key, TimeSpan.FromMinutes(2));
        (await _budget.TryConsumeAsync(scope, "b@example.com", ct)).ShouldBeFalse();
        var ttlAfterRefusal = await db.KeyTimeToLiveAsync(key);

        ttlAfterFirst.ShouldNotBeNull();
        ttlAfterFirst.Value.ShouldBeGreaterThan(TimeSpan.FromMinutes(9));
        ttlAfterFirst.Value.ShouldBeLessThanOrEqualTo(TimeSpan.FromMinutes(10));
        ttlAfterRefusal.ShouldNotBeNull();
        ttlAfterRefusal.Value.ShouldBeLessThanOrEqualTo(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task A_parallel_burst_admits_exactly_the_limit()
    {
        var ct = TestContext.Current.CancellationToken;
        var scope = Scope(5, TimeSpan.FromMinutes(10));

        var answers = await Task.WhenAll(Enumerable.Range(0, 40)
            .Select(_ => _budget.TryConsumeAsync(scope, "d@example.com", ct)));

        answers.Count(a => a).ShouldBe(5);
    }

    [Fact]
    public async Task Two_spellings_of_one_account_share_one_counter()
    {
        // U+017F (long s) upper-cases to 'S', so Identity resolves both spellings to one account.
        var ct = TestContext.Current.CancellationToken;
        var scope = Scope(1, TimeSpan.FromMinutes(10));

        (await _budget.TryConsumeAsync(scope, "sara@example.com", ct)).ShouldBeTrue();
        (await _budget.TryConsumeAsync(scope, "ſara@EXAMPLE.com", ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task Different_addresses_and_different_scopes_keep_separate_counters()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = Scope(1, TimeSpan.FromMinutes(10), "scope-one");
        var second = Scope(1, TimeSpan.FromMinutes(10), "scope-two");

        (await _budget.TryConsumeAsync(first, "e@example.com", ct)).ShouldBeTrue();
        (await _budget.TryConsumeAsync(first, "f@example.com", ct)).ShouldBeTrue();
        (await _budget.TryConsumeAsync(second, "e@example.com", ct)).ShouldBeTrue();
    }

    [Fact]
    public async Task The_key_carries_the_fingerprint_and_never_the_raw_address()
    {
        var ct = TestContext.Current.CancellationToken;
        var scope = Scope(1, TimeSpan.FromMinutes(10), "fingerprint-scope");

        await _budget.TryConsumeAsync(scope, "klas@example.com", ct);

        var keys = _mux.GetServers().Single().Keys(pattern: "jobbliggaren:budget/fingerprint-scope/*")
            .Select(k => k.ToString()).ToList();
        keys.ShouldBe([
            "jobbliggaren:budget/fingerprint-scope/v1/"
            + "fac6ba54474a51ba1a02f54ae399a4998d235bcf2c44f1b3a04ffb4572ac70f8",
        ]);
    }

    [Fact]
    public async Task An_unreachable_redis_throws_the_store_unavailable_contract()
    {
        var ct = TestContext.Current.CancellationToken;
        await _redis.StopAsync(ct);

        var ex = await Should.ThrowAsync<VolatileRedisUnavailableException>(
            () => _budget.TryConsumeAsync(Scope(1, TimeSpan.FromMinutes(1)), "g@example.com", ct));

        ex.ShouldBeAssignableTo<StoreUnavailableException>();

        // Thrown inside the Mediator pipeline, where LoggingBehavior logs the whole exception: a Redis
        // message embeds the operated key, and this key is an address fingerprint. Only the type travels.
        ex.InnerException.ShouldBeNull();
        ex.InnerType.ShouldNotBeNullOrWhiteSpace();
        ex.Message.ShouldNotContain("budget/");
    }
}
