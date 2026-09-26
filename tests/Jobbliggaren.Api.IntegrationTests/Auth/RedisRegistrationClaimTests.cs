using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Auth.Registration;
using Shouldly;
using StackExchange.Redis;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// The registration claim against a real Redis (ADR 0142 D1): one atomic set-if-absent per address, with its
/// lifetime in the same write. Measured on the adapter production registers, on the deploy stack's own
/// <c>redis-volatile</c>. The lifetime is spelled out as a literal, so a change to the policy constant makes
/// this go red.
/// </summary>
public sealed class RedisRegistrationClaimTests : IAsyncLifetime, IClassFixture<SharedVolatileRedisFixture>
{
    private readonly SharedVolatileRedisFixture _redis;

    // The test's OWN reader, beside the connection the claim is given.
    private ConnectionMultiplexer _mux = null!;
    private VolatileRedisConnection _connection = null!;
    private RedisRegistrationClaim _claim = null!;

    public RedisRegistrationClaimTests(SharedVolatileRedisFixture redis) => _redis = redis;

    public async ValueTask InitializeAsync()
    {
        await _redis.FlushAsync();
        var connectionString = $"{_redis.ConnectionString},connectTimeout=1000,syncTimeout=1000";
        _mux = (ConnectionMultiplexer)await ConnectionMultiplexer.ConnectAsync(connectionString);
        _connection = new VolatileRedisConnection(connectionString);
        _claim = new RedisRegistrationClaim(_connection);
    }

    public async ValueTask DisposeAsync()
    {
        _connection.Dispose();
        await _mux.CloseAsync();
        _mux.Dispose();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_first_claim_for_an_address_wins_and_the_next_loses()
    {
        (await _claim.TryClaimAsync("first@example.com", Ct)).ShouldBeTrue();
        (await _claim.TryClaimAsync("first@example.com", Ct)).ShouldBeFalse();
        (await _claim.TryClaimAsync("someone-else@example.com", Ct)).ShouldBeTrue();
    }

    [Fact]
    public async Task Parallel_claims_for_one_address_have_exactly_one_winner()
    {
        var results = await Task.WhenAll(
            Enumerable.Range(0, 20).Select(_ => _claim.TryClaimAsync("race@example.com", Ct)));

        results.Count(won => won).ShouldBe(1);
    }

    [Fact]
    public async Task Two_spellings_that_reach_one_account_share_one_claim()
    {
        (await _claim.TryClaimAsync("Spelling@Example.com", Ct)).ShouldBeTrue();
        (await _claim.TryClaimAsync("  spelling@example.COM ", Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task The_claim_lives_one_minute_from_its_one_write_and_holds_no_address()
    {
        const string email = "reader@example.com";
        await _claim.TryClaimAsync(email, Ct);
        var key = RedisRegistrationClaim.Key(email);

        var ttl = await _mux.GetDatabase().KeyTimeToLiveAsync(key);

        ttl.ShouldNotBeNull().ShouldBeLessThanOrEqualTo(TimeSpan.FromMinutes(1));
        ttl.Value.ShouldBeGreaterThan(TimeSpan.FromSeconds(50));
        key.ShouldNotContain("reader", Case.Insensitive);
        (await _mux.GetDatabase().StringGetAsync(key)).ToString().ShouldBe("1");
    }
}
