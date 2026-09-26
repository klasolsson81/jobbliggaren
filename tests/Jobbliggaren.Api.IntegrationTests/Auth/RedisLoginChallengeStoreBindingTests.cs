using System.Text;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Auth.LoginChallenges;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using StackExchange.Redis;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// #1739 — the BOUND challenge's contract against a real Redis (ADR 0142 D5): a challenge that belongs to a
/// signed-in user and a purpose, in a key family of its own. What is pinned here is what keeps a code from being
/// used by anyone but the user it was minted for, for anything but the purpose it was minted for, in either
/// direction between the bound arm and the login arm. Measured on the adapter production registers, on the
/// deploy stack's own <c>redis-volatile</c>. The attempt count and the lifetime are spelled out as literals.
/// </summary>
public sealed class RedisLoginChallengeStoreBindingTests : IAsyncLifetime, IClassFixture<SharedVolatileRedisFixture>
{
    private readonly SharedVolatileRedisFixture _redis;
    private readonly IDataProtectionProvider _keyring = new EphemeralDataProtectionProvider();

    // The test's OWN reader, beside the connection the store is given.
    private ConnectionMultiplexer _mux = null!;
    private VolatileRedisConnection _connection = null!;
    private RedisLoginChallengeStore _store = null!;

    public RedisLoginChallengeStoreBindingTests(SharedVolatileRedisFixture redis) => _redis = redis;

    public async ValueTask InitializeAsync()
    {
        await _redis.FlushAsync();
        var connectionString = $"{_redis.ConnectionString},connectTimeout=1000,syncTimeout=1000";
        _mux = (ConnectionMultiplexer)await ConnectionMultiplexer.ConnectAsync(connectionString);
        _connection = new VolatileRedisConnection(connectionString);
        _store = Store(_keyring);
    }

    public async ValueTask DisposeAsync()
    {
        _connection.Dispose();
        await _mux.CloseAsync();
        _mux.Dispose();
    }

    private RedisLoginChallengeStore Store(IDataProtectionProvider keyring) =>
        new(_connection, keyring, NullLogger<RedisLoginChallengeStore>.Instance);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ChallengeBinding Reauthentication(Guid userId) => new(ChallengePurpose.Reauthentication, userId);

    private static ChallengeBinding ChangeEmail(Guid userId) => new(ChallengePurpose.ChangeEmail, userId);

    private async Task<(ChallengeId Id, LoginCode Code)> PutBoundAsync(string recipient, ChallengeBinding binding)
    {
        var id = ChallengeId.Generate();
        return (id, await _store.PutBoundAsync(new NewBoundChallenge(id, recipient, binding), Ct));
    }

    private static LoginCode WrongCodeFor(LoginCode code) =>
        LoginCode.FromRaw(code.Reveal() == "000000" ? "111111" : "000000");

    private IEnumerable<string> Keys(string pattern) =>
        _mux.GetServer(_mux.GetEndPoints().Single()).Keys(pattern: pattern).Select(k => k.ToString());

    [Fact]
    public async Task The_right_code_verifies_once_for_its_owner_and_carries_the_records_address()
    {
        var owner = Reauthentication(Guid.NewGuid());
        var (id, code) = await PutBoundAsync("egen@example.se", owner);

        var first = await _store.ConsumeBoundCodeAsync(id, code, owner, Ct);
        var second = await _store.ConsumeBoundCodeAsync(id, code, owner, Ct);

        code.Reveal().ShouldMatch("^[0-9]{6}$");
        first.IsVerified.ShouldBeTrue();
        first.Proof!.ProvenEmail.ShouldBe("egen@example.se");
        second.Outcome.ShouldBe(ChallengeOutcome.Missing);
    }

    [Fact]
    public async Task Three_wrong_codes_burn_the_code_and_the_right_one_stays_burned()
    {
        var owner = Reauthentication(Guid.NewGuid());
        var (id, code) = await PutBoundAsync("burn@example.se", owner);
        var wrong = WrongCodeFor(code);

        var first = await _store.ConsumeBoundCodeAsync(id, wrong, owner, Ct);
        var second = await _store.ConsumeBoundCodeAsync(id, wrong, owner, Ct);
        var third = await _store.ConsumeBoundCodeAsync(id, wrong, owner, Ct);
        var right = await _store.ConsumeBoundCodeAsync(id, code, owner, Ct);

        first.Outcome.ShouldBe(ChallengeOutcome.Wrong);
        first.AttemptsRemaining.ShouldBe(2);
        second.AttemptsRemaining.ShouldBe(1);
        third.Outcome.ShouldBe(ChallengeOutcome.Burned);
        right.Outcome.ShouldBe(ChallengeOutcome.Burned);
    }

    [Fact]
    public async Task Two_wrong_codes_then_the_right_one_on_the_last_attempt_verifies()
    {
        var owner = Reauthentication(Guid.NewGuid());
        var (id, code) = await PutBoundAsync("last@example.se", owner);
        var wrong = WrongCodeFor(code);

        (await _store.ConsumeBoundCodeAsync(id, wrong, owner, Ct)).AttemptsRemaining.ShouldBe(2);
        (await _store.ConsumeBoundCodeAsync(id, wrong, owner, Ct)).AttemptsRemaining.ShouldBe(1);

        (await _store.ConsumeBoundCodeAsync(id, code, owner, Ct)).IsVerified.ShouldBeTrue();
    }

    [Fact]
    public async Task Another_user_with_the_right_code_is_answered_missing_and_the_owner_still_verifies()
    {
        var owner = Reauthentication(Guid.NewGuid());
        var (id, code) = await PutBoundAsync("owner@example.se", owner);

        var asAnother = await _store.ConsumeBoundCodeAsync(id, code, Reauthentication(Guid.NewGuid()), Ct);
        var asOwner = await _store.ConsumeBoundCodeAsync(id, code, owner, Ct);

        // Missing, never Wrong: Wrong would hand the owner's remaining attempts to whoever does not own the record.
        asAnother.Outcome.ShouldBe(ChallengeOutcome.Missing);
        asAnother.AttemptsRemaining.ShouldBe(0);
        asOwner.IsVerified.ShouldBeTrue();
    }

    [Fact]
    public async Task Another_users_presentations_spend_the_records_attempts_all_the_same()
    {
        var owner = Reauthentication(Guid.NewGuid());
        var another = Reauthentication(Guid.NewGuid());
        var (id, code) = await PutBoundAsync("spent@example.se", owner);

        for (var i = 0; i < 3; i++)
            (await _store.ConsumeBoundCodeAsync(id, code, another, Ct)).Outcome.ShouldBe(ChallengeOutcome.Missing);

        // The counter is incremented before anything is compared, and the user id sits inside the protected payload.
        (await _store.ConsumeBoundCodeAsync(id, code, owner, Ct)).Outcome.ShouldBe(ChallengeOutcome.Burned);
    }

    [Fact]
    public async Task Another_purpose_with_the_right_code_is_answered_missing()
    {
        var userId = Guid.NewGuid();
        var (id, code) = await PutBoundAsync("purpose@example.se", Reauthentication(userId));

        (await _store.ConsumeBoundCodeAsync(id, code, ChangeEmail(userId), Ct)).Outcome.ShouldBe(ChallengeOutcome.Missing);
        (await _store.ConsumeBoundCodeAsync(id, code, Reauthentication(userId), Ct)).IsVerified.ShouldBeTrue();
    }

    [Fact]
    public async Task The_login_arm_cannot_see_a_bound_record_and_does_not_touch_it()
    {
        var owner = ChangeEmail(Guid.NewGuid());
        var (id, code) = await PutBoundAsync("ny@example.se", owner);

        Keys("jobbliggaren:auth/challenge/v1/*").ShouldBeEmpty();

        // A change-email code presented on the login arm must never become a login proof.
        (await _store.ConsumeCodeAsync(id, code, Ct)).Outcome.ShouldBe(ChallengeOutcome.Missing);
        (await _store.ConsumeBoundCodeAsync(id, code, owner, Ct)).IsVerified.ShouldBeTrue();
    }

    [Fact]
    public async Task A_login_challenge_cannot_be_verified_as_a_bound_one_and_is_left_untouched()
    {
        // The direction that would be a takeover: an anonymous login challenge to the attacker's own inbox,
        // presented as the proof that a signed-in user owns that address.
        var id = ChallengeId.Generate();
        var issued = await _store.PutAsync(
            new NewLoginChallenge(id, "attacker@example.se", ChallengeCredentials.CodeAndLink, ReplacesLiveChallenge: true), Ct);
        var code = issued.Code!.Value;

        Keys("jobbliggaren:auth/challenge-bound/*").ShouldBeEmpty();

        var asBound = await _store.ConsumeBoundCodeAsync(id, code, ChangeEmail(Guid.NewGuid()), Ct);
        var asLogin = await _store.ConsumeCodeAsync(id, code, Ct);

        asBound.Outcome.ShouldBe(ChallengeOutcome.Missing);
        asLogin.IsVerified.ShouldBeTrue();
    }

    [Fact]
    public async Task A_new_bound_challenge_burns_the_previous_one_of_the_same_user_and_purpose()
    {
        var owner = Reauthentication(Guid.NewGuid());
        var (firstId, firstCode) = await PutBoundAsync("again@example.se", owner);
        var (secondId, secondCode) = await PutBoundAsync("again@example.se", owner);

        (await _store.ConsumeBoundCodeAsync(firstId, firstCode, owner, Ct)).Outcome.ShouldBe(ChallengeOutcome.Missing);
        (await _store.ConsumeBoundCodeAsync(secondId, secondCode, owner, Ct)).IsVerified.ShouldBeTrue();
        Keys("jobbliggaren:auth/challenge-bound/*").ShouldBeEmpty();
    }

    [Fact]
    public async Task Bound_challenges_of_two_purposes_and_of_two_users_do_not_displace_each_other()
    {
        var userId = Guid.NewGuid();
        var reauthentication = await PutBoundAsync("egen@example.se", Reauthentication(userId));
        var changeEmail = await PutBoundAsync("ny@example.se", ChangeEmail(userId));
        var someoneElse = await PutBoundAsync("annan@example.se", Reauthentication(Guid.NewGuid()));

        Keys("jobbliggaren:auth/challenge-bound/*").Count().ShouldBe(3);
        (await _store.ConsumeBoundCodeAsync(reauthentication.Id, reauthentication.Code, Reauthentication(userId), Ct))
            .IsVerified.ShouldBeTrue();
        (await _store.ConsumeBoundCodeAsync(changeEmail.Id, changeEmail.Code, ChangeEmail(userId), Ct))
            .IsVerified.ShouldBeTrue();
        someoneElse.Code.Reveal().ShouldMatch("^[0-9]{6}$");
    }

    [Fact]
    public async Task A_bound_challenge_and_the_addresss_login_challenge_do_not_displace_each_other()
    {
        // An anonymous POST /auth/challenge for the owner's address must not burn the owner's re-authentication,
        // and the reverse.
        const string address = "delad@example.se";
        var owner = Reauthentication(Guid.NewGuid());
        var bound = await PutBoundAsync(address, owner);
        var loginId = ChallengeId.Generate();
        var login = await _store.PutAsync(
            new NewLoginChallenge(loginId, address, ChallengeCredentials.CodeAndLink, ReplacesLiveChallenge: true), Ct);
        var boundAgain = await PutBoundAsync(address, ChangeEmail(owner.UserId));

        (await _store.ConsumeBoundCodeAsync(bound.Id, bound.Code, owner, Ct)).IsVerified.ShouldBeTrue();
        (await _store.ConsumeCodeAsync(loginId, login.Code!.Value, Ct)).IsVerified.ShouldBeTrue();
        boundAgain.Code.Reveal().ShouldMatch("^[0-9]{6}$");
    }

    [Fact]
    public async Task A_bound_record_and_its_index_live_fifteen_minutes()
    {
        var owner = Reauthentication(Guid.NewGuid());
        var (id, _) = await PutBoundAsync("ttl@example.se", owner);
        var db = _mux.GetDatabase();

        var record = await db.KeyTimeToLiveAsync(
            RedisLoginChallengeStore.BoundRecordKey(RedisLoginChallengeStore.RecordSegment(id)));
        var index = await db.KeyTimeToLiveAsync(RedisLoginChallengeStore.BoundIndexKey(owner));

        foreach (var ttl in new[] { record, index })
        {
            ttl.ShouldNotBeNull().ShouldBeLessThanOrEqualTo(TimeSpan.FromMinutes(15));
            ttl.Value.ShouldBeGreaterThan(TimeSpan.FromMinutes(14));
        }
    }

    [Fact]
    public async Task Redis_holds_neither_the_challenge_id_the_address_the_code_nor_the_user_id()
    {
        var userId = Guid.NewGuid();
        const string address = "reader@example.se";
        var (id, code) = await PutBoundAsync(address, ChangeEmail(userId));
        var db = _mux.GetDatabase();

        var keys = Keys("jobbliggaren:auth/challenge-b*").ToList();
        var record = RedisLoginChallengeStore.BoundRecordKey(RedisLoginChallengeStore.RecordSegment(id));
        var stored = Encoding.Latin1.GetString((byte[])(await db.HashGetAsync(record, "p"))!);

        keys.Count.ShouldBe(2);
        keys.ShouldContain(record);
        keys.ShouldContain(RedisLoginChallengeStore.BoundIndexKey(ChangeEmail(userId)));
        foreach (var secret in new[] { id.Reveal(), address, userId.ToString("D"), userId.ToString("N") })
        {
            keys.ShouldAllBe(k => !k.Contains(secret, StringComparison.OrdinalIgnoreCase));
            stored.ShouldNotContain(secret, Case.Insensitive);
        }

        stored.ShouldNotContain(code.Reveal());
    }

    [Fact]
    public async Task The_protected_length_depends_on_the_address_alone()
    {
        const string address = "langd@example.se";
        var db = _mux.GetDatabase();
        var lengths = new List<long>();
        foreach (var binding in new[] { Reauthentication(Guid.NewGuid()), ChangeEmail(Guid.NewGuid()), ChangeEmail(Guid.NewGuid()) })
        {
            var (id, _) = await PutBoundAsync(address, binding);
            lengths.Add(await db.HashStringLengthAsync(
                RedisLoginChallengeStore.BoundRecordKey(RedisLoginChallengeStore.RecordSegment(id)), "p"));
        }

        lengths.Distinct().ShouldHaveSingleItem();

        var (longerId, _) = await PutBoundAsync("en-markbart-langre-adress-an-den-forsta@example.se", Reauthentication(Guid.NewGuid()));
        var longer = await db.HashStringLengthAsync(
            RedisLoginChallengeStore.BoundRecordKey(RedisLoginChallengeStore.RecordSegment(longerId)), "p");
        longer.ShouldBeGreaterThan(lengths[0]);
    }

    [Fact]
    public async Task A_bound_record_written_under_a_lost_keyring_is_answered_missing()
    {
        var owner = Reauthentication(Guid.NewGuid());
        var (id, code) = await PutBoundAsync("keyring@example.se", owner);

        var afterKeyLoss = Store(new EphemeralDataProtectionProvider());

        (await afterKeyLoss.ConsumeBoundCodeAsync(id, code, owner, Ct)).Outcome.ShouldBe(ChallengeOutcome.Missing);
    }
}
