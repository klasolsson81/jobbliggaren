using System.Text;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Auth.ExternalLogins;
using Jobbliggaren.TestSupport;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using StackExchange.Redis;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// #1744 — the OAuth state store's contract against a real Redis (ADR 0142 D1, D8, "Attempt budget": OAuth state
/// ≤ 10 min, the record taken once): single use is <c>GETDEL</c>, the lifetime is the key's TTL, and a Redis reader
/// learns neither the state nor the verifier. Measured on the adapter production will register, on the deploy
/// stack's own <c>redis-volatile</c>. The lifetime is spelled out as a literal 10.
/// </summary>
public sealed class RedisOAuthStateStoreTests : IAsyncLifetime, IClassFixture<SharedVolatileRedisFixture>
{
    private readonly SharedVolatileRedisFixture _redis;
    private readonly EphemeralDataProtectionProvider _keyring = new();

    private ConnectionMultiplexer _mux = null!;
    private VolatileRedisConnection _connection = null!;
    private RedisOAuthStateStore _store = null!;

    public RedisOAuthStateStoreTests(SharedVolatileRedisFixture redis) => _redis = redis;

    public async ValueTask InitializeAsync()
    {
        await _redis.FlushAsync();
        var connectionString = $"{_redis.ConnectionString},connectTimeout=1000,syncTimeout=1000";
        _mux = (ConnectionMultiplexer)await ConnectionMultiplexer.ConnectAsync(connectionString);
        _connection = new VolatileRedisConnection(connectionString);
        _store = new RedisOAuthStateStore(_connection, _keyring, NullLogger<RedisOAuthStateStore>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        _connection.Dispose();
        await _mux.CloseAsync();
        _mux.Dispose();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static OAuthFlow Flow(string next = "/ansokningar/abc-123") =>
        new(ExternalProviderKey.Google, PkceVerifier.Generate(), next);

    [Fact]
    public async Task A_flow_is_taken_once_and_hands_back_its_provider_verifier_and_path()
    {
        var flow = Flow();
        var state = await _store.PutAsync(flow, Ct);

        var first = await _store.TakeAsync(state, ExternalProviderKey.Google, Ct);
        var second = await _store.TakeAsync(state, ExternalProviderKey.Google, Ct);

        first.ShouldNotBeNull();
        first.Provider.ShouldBe(ExternalProviderKey.Google);
        first.Verifier.Reveal().ShouldBe(flow.Verifier.Reveal());
        first.Next.ShouldBe("/ansokningar/abc-123");
        second.ShouldBeNull();
    }

    [Fact]
    public async Task Parallel_takes_of_one_state_have_exactly_one_winner()
    {
        var state = await _store.PutAsync(Flow(), Ct);

        var results = await Task.WhenAll(
            Enumerable.Range(0, 20).Select(_ => _store.TakeAsync(state, ExternalProviderKey.Google, Ct)));

        results.Count(r => r is not null).ShouldBe(1);
    }

    [Fact]
    public async Task A_state_nobody_started_takes_to_nothing() =>
        (await _store.TakeAsync(OAuthState.Generate(), ExternalProviderKey.Google, Ct)).ShouldBeNull();

    [Fact]
    public async Task A_flow_lives_ten_minutes_from_its_one_write()
    {
        var state = await _store.PutAsync(Flow(), Ct);

        var ttl = await _mux.GetDatabase().KeyTimeToLiveAsync(RedisOAuthStateStore.Key(state));

        ttl.ShouldNotBeNull().ShouldBeLessThanOrEqualTo(TimeSpan.FromMinutes(10));
        ttl.Value.ShouldBeGreaterThan(TimeSpan.FromMinutes(9));
    }

    [Fact]
    public async Task Redis_holds_neither_the_state_nor_the_verifier_nor_the_path()
    {
        var flow = Flow("/ansokningar/secret-path-123");
        var state = await _store.PutAsync(flow, Ct);
        var server = _mux.GetServer(_mux.GetEndPoints().Single());

        var keys = server.Keys(pattern: "jobbliggaren:auth/oauth-state/*").Select(k => k.ToString()).ToList();

        keys.ShouldHaveSingleItem().ShouldBe(RedisOAuthStateStore.Key(state));
        keys[0].ShouldNotContain(state.Reveal());
        var stored = Encoding.Latin1.GetString((byte[])(await _mux.GetDatabase().StringGetAsync(keys[0]))!);
        stored.ShouldNotContain(state.Reveal());
        stored.ShouldNotContain(flow.Verifier.Reveal());
        stored.ShouldNotContain("secret-path-123");
        stored.ShouldNotContain("google");
    }

    [Fact]
    public async Task A_flow_written_under_a_lost_keyring_takes_to_nothing_and_is_logged_once()
    {
        var state = await _store.PutAsync(Flow(), Ct);
        var logger = new RecordingLogger<RedisOAuthStateStore>();

        var afterKeyLoss = new RedisOAuthStateStore(_connection, new EphemeralDataProtectionProvider(), logger);

        (await afterKeyLoss.TakeAsync(state, ExternalProviderKey.Google, Ct)).ShouldBeNull();
        logger.Records.Count(r => r.EventId.Id == 1026).ShouldBe(1);
    }

    /// <summary>
    /// UNREACHABLE STATE in 6a, declared: a flow started for a provider this build does not know, or with no
    /// verifier. Google is the only key, so no path in <c>src/</c> writes either; each record is written by hand
    /// under the adapter's key and protector, and the test asserts only that the read refuses it AND spends it
    /// (GETDEL before the compare). The mismatch between two known providers becomes reachable in 6b.
    /// </summary>
    [Fact]
    public async Task A_flow_for_another_provider_or_without_a_verifier_is_refused_and_spent()
    {
        var foreign = OAuthState.Generate();
        var noVerifier = OAuthState.Generate();
        await WriteByHandAsync(foreign, new { p = "myspace", v = "verifier", n = "/" });
        await WriteByHandAsync(noVerifier, new { p = "google", n = "/" });

        (await _store.TakeAsync(foreign, ExternalProviderKey.Google, Ct)).ShouldBeNull();
        (await _store.TakeAsync(noVerifier, ExternalProviderKey.Google, Ct)).ShouldBeNull();
        (await _mux.GetDatabase().KeyExistsAsync(RedisOAuthStateStore.Key(foreign))).ShouldBeFalse();
    }

    private async Task WriteByHandAsync(OAuthState state, object record)
    {
        var payload = _keyring
            .CreateProtector(RedisOAuthStateStore.ProtectorPurpose)
            .Protect(JsonSerializer.SerializeToUtf8Bytes(record));
        await _mux.GetDatabase().StringSetAsync(RedisOAuthStateStore.Key(state), payload, TimeSpan.FromMinutes(10));
    }
}
