using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Auth.Commands.StartExternalLogin;
using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Auth.ExternalLogins;
using Jobbliggaren.Infrastructure.Auth.LoginChallenges;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// #1735 — what a FULL <c>redis-volatile</c> answers, on the deploy stack's own declaration
/// (<see cref="VolatileRedisContainer.FromDeployCompose"/>): <c>maxmemory</c>, <c>noeviction</c>, the tmpfs
/// and the cgroup limits exactly as <c>deploy/docker-compose.yml</c> writes them. The actor that fills it
/// is a flood of writes, which is what production's would be.
///
/// <para>
/// A retention assertion as much as a robustness one: the processing register's retention row rests on "a
/// budget key never exists without a TTL", and both stores write their keys inside <c>MULTI</c>. If a
/// refused transaction left its <c>INCR</c> behind without its <c>EXPIRE</c>, that key would never expire.
/// </para>
/// </summary>
public sealed class VolatileRedisOutOfMemoryTests : IAsyncLifetime
{
    private const string BudgetKeys = "jobbliggaren:budget/*";
    private const string ChallengeKeys = "jobbliggaren:auth/*";

    private readonly RedisContainer _redis = VolatileRedisContainer.FromDeployCompose();

    // The test's OWN reader, beside the connection the adapters are given (see RedisRateBudgetTests).
    private ConnectionMultiplexer _mux = null!;
    private VolatileRedisConnection _connection = null!;
    private RedisRateBudget _budget = null!;
    private RedisLoginChallengeStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        await _redis.StartAsync();
        var connectionString = $"{VolatileRedisContainer.OperatorConnectionString(_redis)},connectTimeout=1000,syncTimeout=5000";
        _mux = (ConnectionMultiplexer)await ConnectionMultiplexer.ConnectAsync(connectionString);
        _connection = new VolatileRedisConnection(connectionString);
        _budget = new RedisRateBudget(_connection);
        _store = new RedisLoginChallengeStore(
            _connection, new EphemeralDataProtectionProvider(), NullLogger<RedisLoginChallengeStore>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        _connection.Dispose();
        await _mux.CloseAsync();
        _mux.Dispose();
        await VolatileRedisContainer.DisposeAsync(_redis);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static RateBudgetScope Scope => new("oom-scope", 5, TimeSpan.FromHours(24));

    /// <summary>
    /// Grows the dataset 1 MiB per command until Redis itself refuses one, and returns how many it took.
    /// SETRANGE rather than a 1 MiB SET: Redis measures memory BEFORE a command runs and counts the client's
    /// query buffer in it, so a large SET is refused while a small write still fits and the instance is not
    /// full. A few bytes of SETRANGE allocate the whole mebibyte, which takes the dataset OVER the limit.
    /// </summary>
    private async Task<int> FillUntilRefusedAsync()
    {
        var db = _mux.GetDatabase();
        for (var i = 0; i < 1024; i++)
        {
            try
            {
                await db.StringSetRangeAsync($"flood:{i}", (1024 * 1024) - 1, "x");
            }
            catch (RedisServerException ex) when (ex.Message.StartsWith("OOM", StringComparison.Ordinal))
            {
                return i;
            }
        }

        throw new InvalidOperationException("The instance accepted 1 GiB: maxmemory is not in force.");
    }

    private async Task<List<RedisKey>> KeysAsync(string pattern)
    {
        var server = _mux.GetServer(_mux.GetEndPoints().Single());
        var keys = new List<RedisKey>();
        await foreach (var key in server.KeysAsync(pattern: pattern).WithCancellation(Ct))
            keys.Add(key);

        return keys;
    }

    [Fact]
    public async Task A_full_instance_refuses_both_stores_as_the_translated_fault_and_leaves_no_key_without_a_ttl()
    {
        var db = _mux.GetDatabase();

        (await _budget.TryConsumeAsync(Scope, "before@example.com", Ct)).ShouldBeTrue();
        var before = RedisRateBudget.Key(Scope, "before@example.com");

        (await FillUntilRefusedAsync()).ShouldBeGreaterThan(0);

        await Should.ThrowAsync<VolatileRedisUnavailableException>(
            () => _budget.TryConsumeAsync(Scope, "after@example.com", Ct));
        await Should.ThrowAsync<VolatileRedisUnavailableException>(
            () => _store.PutAsync(
                new NewLoginChallenge(ChallengeId.Generate(), "after@example.com", ChallengeCredentials.CodeAndLink, true),
                Ct));

        // The refused writes left nothing behind...
        (await db.KeyExistsAsync(RedisRateBudget.Key(Scope, "after@example.com"))).ShouldBeFalse();
        (await KeysAsync(ChallengeKeys)).ShouldBeEmpty();

        // ...and every budget key that does exist still has its TTL.
        var budgetKeys = await KeysAsync(BudgetKeys);
        budgetKeys.ShouldBe([(RedisKey)before]);
        foreach (var key in budgetKeys)
            (await db.KeyTimeToLiveAsync(key)).ShouldNotBeNull($"{key} has no TTL");

        // The counter written BEFORE the flood is still there with its count. This instance persists nothing,
        // so a container the cgroup had killed and restarted would have come back empty: the flood was met by
        // Redis's own refusal, which is what the sizing relation exists for.
        ((int)await db.StringGetAsync(before)).ShouldBe(1);
    }

    /// <summary>
    /// #1744 (security-auditor PR S m3) — the external-login start's bound. The actor is the start path over every
    /// budget window a flow can outlive: <see cref="ExternalLoginPolicy.StartBudget"/> admits its limit per window
    /// and each flow lives <see cref="ExternalLoginPolicy.StateTtl"/>, so no more flows than written here are ever
    /// live at once. Each carries the longest path the start validator admits, in the character the JSON writer
    /// escapes to six bytes, through the production store.
    /// </summary>
    [Fact]
    public async Task The_start_path_at_its_budget_bound_takes_at_most_a_sixteenth_of_the_instance()
    {
        var budget = ExternalLoginPolicy.StartBudget;
        var live = budget.Limit * ((int)Math.Ceiling(ExternalLoginPolicy.StateTtl / budget.Window) + 1);
        var next = "/" + new string('&', ExternalLoginPolicy.MaxNextLength - 1);
        new StartExternalLoginCommandValidator().Validate(new StartExternalLoginCommand("google", next)).IsValid
            .ShouldBeTrue();
        var flows = new RedisOAuthStateStore(
            _connection, new EphemeralDataProtectionProvider(), NullLogger<RedisOAuthStateStore>.Instance);
        await using var admin = await ConnectionMultiplexer.ConnectAsync(
            $"{VolatileRedisContainer.OperatorConnectionString(_redis)},allowAdmin=true");
        var server = admin.GetServer(admin.GetEndPoints().Single());
        var before = await MemoryAsync(server, "used_memory");

        for (var i = 0; i < live; i++)
            await flows.PutAsync(new OAuthFlow(ExternalProviderKey.Google, PkceVerifier.Generate(), next), Ct);

        var taken = await MemoryAsync(server, "used_memory") - before;
        var maxmemory = await MemoryAsync(server, "maxmemory");
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{live} flows took {taken} of {maxmemory} bytes ({100.0 * taken / maxmemory:F1} %)");
        taken.ShouldBeLessThanOrEqualTo(maxmemory / 16);
        await _store.PutAsync(
            new NewLoginChallenge(ChallengeId.Generate(), "after-flows@example.com", ChallengeCredentials.CodeAndLink, true),
            Ct);
    }

    private static async Task<long> MemoryAsync(IServer server, string field) =>
        long.Parse(
            (await server.InfoAsync("memory")).SelectMany(group => group).Single(pair => pair.Key == field).Value,
            System.Globalization.CultureInfo.InvariantCulture);
}
