using System.Text;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Auth.Grants;
using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Auth.Grants;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// The grant store's contract against a real Redis (ADR 0142 D3): single use is <c>GETDEL</c>, the lifetime is
/// the key's TTL, and a Redis reader learns neither the token nor the address. Measured on the adapter
/// production registers, on the deploy stack's own <c>redis-volatile</c>. The lifetime is spelled out as a
/// literal 10, so a change to the policy constant makes this go red.
/// </summary>
public sealed class RedisGrantStoreTests : IAsyncLifetime
{
    private readonly RedisContainer _redis = VolatileRedisContainer.FromDeployCompose();
    private readonly EphemeralDataProtectionProvider _keyring = new();

    // The test's OWN reader, beside the connection the store is given.
    private ConnectionMultiplexer _mux = null!;
    private VolatileRedisConnection _connection = null!;
    private RedisGrantStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        await _redis.StartAsync();
        var connectionString = $"{_redis.GetConnectionString()},connectTimeout=1000,syncTimeout=1000";
        _mux = (ConnectionMultiplexer)await ConnectionMultiplexer.ConnectAsync(connectionString);
        _connection = new VolatileRedisConnection(connectionString);
        _store = Store(_keyring);
    }

    public async ValueTask DisposeAsync()
    {
        _connection.Dispose();
        await _mux.CloseAsync();
        _mux.Dispose();
        await _redis.DisposeAsync();
    }

    private RedisGrantStore Store(IDataProtectionProvider keyring) =>
        new(_connection, keyring, NullLogger<RedisGrantStore>.Instance);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static GrantAssertion Bearer => GrantAssertion.Bearer(GrantPurpose.LoginComplete);

    [Fact]
    public async Task A_grant_is_redeemed_once_and_hands_back_the_proven_address()
    {
        var token = await _store.IssueAsync(new GrantSubject.LoginComplete("once@example.com"), Ct);

        var first = await _store.RedeemAsync(token, Bearer, Ct);
        var second = await _store.RedeemAsync(token, Bearer, Ct);

        first.ShouldBeOfType<GrantSubject.LoginComplete>().ProvenEmail.ShouldBe("once@example.com");
        second.ShouldBeNull();
    }

    [Fact]
    public async Task Parallel_redemptions_of_one_grant_have_exactly_one_winner()
    {
        var token = await _store.IssueAsync(new GrantSubject.LoginComplete("race@example.com"), Ct);

        var results = await Task.WhenAll(
            Enumerable.Range(0, 20).Select(_ => _store.RedeemAsync(token, Bearer, Ct)));

        results.Count(r => r is not null).ShouldBe(1);
    }

    [Fact]
    public async Task A_token_nobody_issued_redeems_to_nothing()
    {
        (await _store.RedeemAsync(GrantToken.Generate(), Bearer, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task A_grant_lives_ten_minutes_from_its_one_write()
    {
        var token = await _store.IssueAsync(new GrantSubject.LoginComplete("ttl@example.com"), Ct);

        var ttl = await _mux.GetDatabase().KeyTimeToLiveAsync(RedisGrantStore.Key(token));

        ttl.ShouldNotBeNull().ShouldBeLessThanOrEqualTo(TimeSpan.FromMinutes(10));
        ttl.Value.ShouldBeGreaterThan(TimeSpan.FromMinutes(9));
    }

    [Fact]
    public async Task A_binding_the_caller_asserts_must_equal_the_stored_subject()
    {
        var subject = new GrantSubject.LoginComplete("bound@example.com");
        var refused = await _store.IssueAsync(subject, Ct);
        var admitted = await _store.IssueAsync(subject, Ct);

        (await _store.RedeemAsync(
            refused, GrantAssertion.Of(new GrantSubject.LoginComplete("other@example.com")), Ct)).ShouldBeNull();
        (await _store.RedeemAsync(admitted, GrantAssertion.Of(subject), Ct)).ShouldBe(subject);
    }

    [Fact]
    public async Task A_grant_written_under_a_lost_keyring_redeems_to_nothing()
    {
        var token = await _store.IssueAsync(new GrantSubject.LoginComplete("keyring@example.com"), Ct);

        var afterKeyLoss = Store(new EphemeralDataProtectionProvider());

        (await afterKeyLoss.RedeemAsync(token, Bearer, Ct)).ShouldBeNull();
    }

    /// <summary>
    /// UNREACHABLE STATE, declared: a record whose purpose number this build does not define. No path in
    /// <c>src/</c> writes one — <see cref="GrantPurpose"/> has one member and the adapter serialises only
    /// that — so the record is written by hand, under the key and the protector the adapter uses, and the
    /// test asserts only that the read side degrades to "no grant".
    /// </summary>
    [Fact]
    public async Task A_record_with_a_purpose_this_build_does_not_define_redeems_to_nothing()
    {
        var token = GrantToken.Generate();
        var control = GrantToken.Generate();
        await WriteByHandAsync(token, purpose: 99, "future@example.com");
        await WriteByHandAsync(control, purpose: 1, "control@example.com");

        (await _store.RedeemAsync(token, Bearer, Ct)).ShouldBeNull();
        (await _store.RedeemAsync(control, Bearer, Ct)).ShouldBe(new GrantSubject.LoginComplete("control@example.com"));
    }

    private async Task WriteByHandAsync(GrantToken token, int purpose, string email)
    {
        var payload = _keyring
            .CreateProtector(RedisGrantStore.ProtectorPurpose)
            .CreateProtector("1")
            .Protect(JsonSerializer.SerializeToUtf8Bytes(new { p = purpose, e = email }));
        await _mux.GetDatabase().StringSetAsync(RedisGrantStore.Key(token), payload, TimeSpan.FromMinutes(10));
    }

    [Fact]
    public async Task Redis_holds_neither_the_raw_token_nor_the_address()
    {
        const string email = "reader@example.com";
        var token = await _store.IssueAsync(new GrantSubject.LoginComplete(email), Ct);
        var server = _mux.GetServer(_mux.GetEndPoints().Single());

        var keys = server.Keys(pattern: "jobbliggaren:auth/grant/*").Select(k => k.ToString()).ToList();

        keys.ShouldHaveSingleItem().ShouldBe(RedisGrantStore.Key(token));
        keys.ShouldAllBe(k => !k.Contains(token.Reveal(), StringComparison.Ordinal));
        var stored = Encoding.Latin1.GetString((byte[])(await _mux.GetDatabase().StringGetAsync(keys[0]))!);
        stored.ShouldNotContain(email);
        stored.ShouldNotContain(token.Reveal());
    }
}
