using System.Buffers.Text;
using System.Globalization;
using System.Text;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Auth.LoginChallenges;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// The login challenge store's contract against a real Redis (ADR 0142 D1). Every property here is Redis
/// semantics — the increment before the compare, delete-once as single use, the TTL, the existence guard —
/// so it is measured on the adapter production registers, never on an in-memory copy. The attempt count is
/// spelled out as a literal 3, so a change to the policy constant makes these go red.
/// </summary>
public sealed class RedisLoginChallengeStoreTests : IAsyncLifetime, IClassFixture<SharedVolatileRedisFixture>
{
    // The deploy stack's own `redis-volatile`, so the contract is measured on the configuration the box runs.
    private readonly SharedVolatileRedisFixture _redis;
    private readonly IDataProtectionProvider _keyring = new EphemeralDataProtectionProvider();

    // The test's OWN reader, beside the connection the store is given (see RedisRateBudgetTests).
    private ConnectionMultiplexer _mux = null!;
    private VolatileRedisConnection _connection = null!;
    private RedisLoginChallengeStore _store = null!;

    public RedisLoginChallengeStoreTests(SharedVolatileRedisFixture redis) => _redis = redis;

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

    private async Task<(ChallengeId Id, IssuedCredentials Issued)> PutAsync(
        string email,
        ChallengeCredentials credentials = ChallengeCredentials.CodeAndLink,
        bool replaces = true,
        RedisLoginChallengeStore? store = null)
    {
        var id = ChallengeId.Generate();
        var issued = await (store ?? _store).PutAsync(new NewLoginChallenge(id, email, credentials, replaces), Ct);
        return (id, issued);
    }

    private static LoginCode WrongCodeFor(LoginCode code) =>
        LoginCode.FromRaw(code.Reveal() == "000000" ? "111111" : "000000");

    [Fact]
    public async Task A_code_and_link_challenge_mints_a_six_digit_code_and_a_link()
    {
        var (_, issued) = await PutAsync("mint@example.com");

        issued.Code.ShouldNotBeNull();
        issued.Code.Value.Reveal().ShouldMatch("^[0-9]{6}$");
        issued.Link.ShouldNotBeNull();
    }

    [Fact]
    public async Task The_right_code_verifies_once_and_carries_the_proven_address()
    {
        var (id, issued) = await PutAsync("once@example.com");

        var first = await _store.ConsumeCodeAsync(id, issued.Code!.Value, Ct);
        var second = await _store.ConsumeCodeAsync(id, issued.Code!.Value, Ct);

        first.IsVerified.ShouldBeTrue();
        first.Proof!.ProvenEmail.ShouldBe("once@example.com");
        second.Outcome.ShouldBe(ChallengeOutcome.Missing);
    }

    [Fact]
    public async Task Three_wrong_codes_burn_the_code_and_a_fourth_try_and_the_right_code_stay_burned()
    {
        var (id, issued) = await PutAsync("burn@example.com");
        var wrong = WrongCodeFor(issued.Code!.Value);

        var one = await _store.ConsumeCodeAsync(id, wrong, Ct);
        var two = await _store.ConsumeCodeAsync(id, wrong, Ct);
        var three = await _store.ConsumeCodeAsync(id, wrong, Ct);
        var four = await _store.ConsumeCodeAsync(id, wrong, Ct);
        var right = await _store.ConsumeCodeAsync(id, issued.Code!.Value, Ct);

        one.Outcome.ShouldBe(ChallengeOutcome.Wrong);
        one.AttemptsRemaining.ShouldBe(2);
        two.Outcome.ShouldBe(ChallengeOutcome.Wrong);
        two.AttemptsRemaining.ShouldBe(1);
        three.Outcome.ShouldBe(ChallengeOutcome.Burned);
        four.Outcome.ShouldBe(ChallengeOutcome.Burned);
        right.Outcome.ShouldBe(ChallengeOutcome.Burned);
    }

    [Fact]
    public async Task Three_wrong_codes_leave_the_link_consumable()
    {
        // Under Klas's (A) the owner's relief from a third party draining the budget is the link in the mail.
        // The third party holds the challenge id and could spend the three guesses; if that killed the link,
        // every link-only mail would arrive dead (dotnet-architect R1, security-auditor Q-S1).
        var (id, issued) = await PutAsync("link-after-burn@example.com");
        var wrong = WrongCodeFor(issued.Code!.Value);
        for (var i = 0; i < 3; i++)
            await _store.ConsumeCodeAsync(id, wrong, Ct);

        var proof = await _store.ConsumeLinkAsync(issued.Link!.Value, Ct);

        proof.ShouldNotBeNull();
        proof.ProvenEmail.ShouldBe("link-after-burn@example.com");
    }

    [Fact]
    public async Task The_link_consumes_once_and_then_the_code_is_gone_too()
    {
        var (id, issued) = await PutAsync("link-first@example.com");

        var first = await _store.ConsumeLinkAsync(issued.Link!.Value, Ct);
        var again = await _store.ConsumeLinkAsync(issued.Link!.Value, Ct);
        var code = await _store.ConsumeCodeAsync(id, issued.Code!.Value, Ct);

        first.ShouldNotBeNull();
        again.ShouldBeNull();
        code.Outcome.ShouldBe(ChallengeOutcome.Missing);
    }

    [Fact]
    public async Task The_code_consumes_and_then_the_link_is_gone_too()
    {
        var (id, issued) = await PutAsync("code-first@example.com");

        (await _store.ConsumeCodeAsync(id, issued.Code!.Value, Ct)).IsVerified.ShouldBeTrue();

        (await _store.ConsumeLinkAsync(issued.Link!.Value, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task A_wrong_link_secret_never_spends_the_codes_attempts()
    {
        var (id, issued) = await PutAsync("scanner@example.com");
        var token = Base64UrlBytes(issued.Link!.Value.Reveal());
        token[^1] ^= 0xFF; // same challenge id, different secret
        var forged = LoginLinkToken.FromRaw(System.Buffers.Text.Base64Url.EncodeToString(token));

        for (var i = 0; i < 5; i++)
            (await _store.ConsumeLinkAsync(forged, Ct)).ShouldBeNull();

        var afterScan = await _store.ConsumeCodeAsync(id, WrongCodeFor(issued.Code!.Value), Ct);
        afterScan.Outcome.ShouldBe(ChallengeOutcome.Wrong);
        afterScan.AttemptsRemaining.ShouldBe(2);
    }

    [Fact]
    public async Task A_link_only_record_answers_codes_like_a_wrong_code_and_its_link_works()
    {
        var (id, issued) = await PutAsync("link-only@example.com", ChallengeCredentials.LinkOnly, replaces: false);

        issued.Code.ShouldBeNull();
        var one = await _store.ConsumeCodeAsync(id, LoginCode.FromRaw("000000"), Ct);
        var two = await _store.ConsumeCodeAsync(id, LoginCode.FromRaw("123456"), Ct);
        var three = await _store.ConsumeCodeAsync(id, LoginCode.FromRaw("123456"), Ct);
        var link = await _store.ConsumeLinkAsync(issued.Link!.Value, Ct);

        (one.Outcome, one.AttemptsRemaining).ShouldBe((ChallengeOutcome.Wrong, 2));
        (two.Outcome, two.AttemptsRemaining).ShouldBe((ChallengeOutcome.Wrong, 1));
        three.Outcome.ShouldBe(ChallengeOutcome.Burned);
        link.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_code_only_record_mints_a_code_that_verifies_and_no_link()
    {
        var (id, issued) = await PutAsync("code-only@example.com", ChallengeCredentials.CodeOnly, replaces: false);

        issued.Link.ShouldBeNull();
        (await _store.ConsumeCodeAsync(id, issued.Code!.Value, Ct)).IsVerified.ShouldBeTrue();
    }

    [Fact]
    public async Task A_record_without_credentials_answers_codes_like_a_wrong_code_and_has_no_link()
    {
        var (id, issued) = await PutAsync("closed@example.com", ChallengeCredentials.None);

        issued.ShouldBe(new IssuedCredentials(null, null));
        var one = await _store.ConsumeCodeAsync(id, LoginCode.FromRaw("000000"), Ct);
        (one.Outcome, one.AttemptsRemaining).ShouldBe((ChallengeOutcome.Wrong, 2));
    }

    [Fact]
    public async Task An_unknown_challenge_is_missing_and_the_guard_creates_no_key()
    {
        var unknown = ChallengeId.Generate();

        var verdict = await _store.ConsumeCodeAsync(unknown, LoginCode.FromRaw("123456"), Ct);

        verdict.Outcome.ShouldBe(ChallengeOutcome.Missing);
        (await _mux.GetDatabase().KeyExistsAsync(
            RedisLoginChallengeStore.RecordKey(RedisLoginChallengeStore.RecordSegment(unknown)))).ShouldBeFalse();
    }

    [Fact]
    public async Task The_record_and_the_address_index_live_fifteen_minutes()
    {
        var (id, _) = await PutAsync("ttl@example.com");
        var db = _mux.GetDatabase();

        var recordTtl = await db.KeyTimeToLiveAsync(
            RedisLoginChallengeStore.RecordKey(RedisLoginChallengeStore.RecordSegment(id)));
        var indexTtl = await db.KeyTimeToLiveAsync(RedisLoginChallengeStore.IndexKey("ttl@example.com"));

        recordTtl.ShouldNotBeNull();
        recordTtl.Value.ShouldBeGreaterThan(TimeSpan.FromMinutes(14));
        recordTtl.Value.ShouldBeLessThanOrEqualTo(TimeSpan.FromMinutes(15));
        indexTtl.ShouldNotBeNull();
        indexTtl.Value.ShouldBeLessThanOrEqualTo(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public async Task A_replacing_put_burns_the_addresss_previous_challenge()
    {
        var (firstId, first) = await PutAsync("one-live@example.com");
        await PutAsync("one-live@example.com");

        (await _store.ConsumeCodeAsync(firstId, first.Code!.Value, Ct)).Outcome.ShouldBe(ChallengeOutcome.Missing);
        (await _store.ConsumeLinkAsync(first.Link!.Value, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task A_put_that_does_not_replace_leaves_the_live_challenge_alone()
    {
        // The code budget is spent: the link-only mail must not burn the code the owner already holds.
        var (liveId, live) = await PutAsync("keep-live@example.com");
        await PutAsync("keep-live@example.com", ChallengeCredentials.LinkOnly, replaces: false);

        (await _store.ConsumeCodeAsync(liveId, live.Code!.Value, Ct)).IsVerified.ShouldBeTrue();
    }

    [Fact]
    public async Task A_replacing_put_for_another_spelling_of_the_address_burns_the_same_challenge()
    {
        var (firstId, first) = await PutAsync("sara@example.com");
        await PutAsync("  ſARA@example.com ");

        (await _store.ConsumeCodeAsync(firstId, first.Code!.Value, Ct)).Outcome.ShouldBe(ChallengeOutcome.Missing);
    }

    [Fact]
    public async Task A_parallel_burst_of_right_codes_verifies_exactly_once()
    {
        var (id, issued) = await PutAsync("burst-right@example.com");

        var verdicts = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => _store.ConsumeCodeAsync(id, issued.Code!.Value, Ct)));

        verdicts.Count(v => v.IsVerified).ShouldBe(1);
    }

    [Fact]
    public async Task A_parallel_burst_of_the_right_link_proves_exactly_once()
    {
        var (_, issued) = await PutAsync("burst-link@example.com");

        var proofs = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => _store.ConsumeLinkAsync(issued.Link!.Value, Ct)));

        proofs.Count(p => p is not null).ShouldBe(1);
    }

    [Fact]
    public async Task A_right_code_racing_the_right_link_proves_exactly_once()
    {
        for (var round = 0; round < 10; round++)
        {
            var (id, issued) = await PutAsync($"race-{round}@example.com");

            var code = Task.Run(() => _store.ConsumeCodeAsync(id, issued.Code!.Value, Ct), Ct);
            var link = Task.Run(() => _store.ConsumeLinkAsync(issued.Link!.Value, Ct), Ct);
            var (codeVerdict, linkProof) = (await code, await link);

            ((codeVerdict.IsVerified ? 1 : 0) + (linkProof is null ? 0 : 1)).ShouldBe(1);
        }
    }

    [Fact]
    public async Task A_minted_link_token_is_the_challenge_id_and_a_128_bit_secret()
    {
        // The requester holds the id, so the secret is the whole of what a forger must guess: 128 bits is
        // why the link needs no attempt budget (ADR 0142 D1).
        var (id, issued) = await PutAsync("secret-width@example.com");

        var token = Base64Url.DecodeFromChars(issued.Link!.Value.Reveal());

        token.Length.ShouldBe(32);
        token[..16].ShouldBe(Base64Url.DecodeFromChars(id.Reveal()));
    }

    [Fact]
    public async Task Minted_codes_span_the_full_six_digit_space()
    {
        // The guess arithmetic rests on 10^6 codes. A narrower draw zero-pads to six digits and would pass
        // the shape check; 64 draws all below 100000 happen with probability 10^-64 from the full space.
        var codes = new List<int>();
        for (var i = 0; i < 64; i++)
            codes.Add(int.Parse(
                (await PutAsync($"space-{i}@example.com")).Issued.Code!.Value.Reveal(), CultureInfo.InvariantCulture));

        codes.Max().ShouldBeGreaterThanOrEqualTo(100_000);
    }

    [Fact]
    public async Task Every_record_for_one_address_protects_to_one_length()
    {
        // A Redis reader who knows the address can find its records; their length must not say which
        // credentials they carry, and so whether the address has an account.
        const string email = "one-length@example.com";
        var db = _mux.GetDatabase();
        var lengths = new List<long>();
        foreach (var credentials in Enum.GetValues<ChallengeCredentials>())
        {
            var (id, _) = await PutAsync(email, credentials, replaces: false);
            lengths.Add(((byte[])(await db.HashGetAsync(
                RedisLoginChallengeStore.RecordKey(RedisLoginChallengeStore.RecordSegment(id)), "p"))!).Length);
        }

        lengths.Distinct().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_parallel_burst_of_wrong_codes_counts_every_attempt_and_never_verifies()
    {
        var (id, issued) = await PutAsync("burst-wrong@example.com");
        var wrong = WrongCodeFor(issued.Code!.Value);

        var verdicts = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => _store.ConsumeCodeAsync(id, wrong, Ct)));

        verdicts.ShouldNotContain(v => v.IsVerified);
        verdicts.Count(v => v.Outcome == ChallengeOutcome.Wrong).ShouldBe(2);
        ((long)await _mux.GetDatabase().HashGetAsync(
            RedisLoginChallengeStore.RecordKey(RedisLoginChallengeStore.RecordSegment(id)), "a"))
            .ShouldBe(20);
        (await _store.ConsumeCodeAsync(id, issued.Code!.Value, Ct)).Outcome.ShouldBe(ChallengeOutcome.Burned);
    }

    [Fact]
    public async Task A_lost_keyring_degrades_every_live_challenge_to_missing_without_throwing()
    {
        var (id, issued) = await PutAsync("keyring@example.com");
        var afterLoss = Store(new EphemeralDataProtectionProvider());

        (await afterLoss.ConsumeCodeAsync(id, issued.Code!.Value, Ct)).Outcome.ShouldBe(ChallengeOutcome.Missing);
        (await afterLoss.ConsumeLinkAsync(issued.Link!.Value, Ct)).ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base64-at-all!!")]
    [InlineData("AAAA")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task A_malformed_link_token_is_the_one_link_failure_and_never_throws(string raw)
    {
        (await _store.ConsumeLinkAsync(LoginLinkToken.FromRaw(raw), Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Redis_holds_no_raw_address_code_or_challenge_id()
    {
        const string email = "raw-check@example.com";
        var (id, issued) = await PutAsync(email);
        var server = _mux.GetServers().Single();
        var db = _mux.GetDatabase();

        foreach (var key in server.Keys(pattern: "jobbliggaren:auth/*"))
        {
            var name = key.ToString();
            name.ShouldNotContain(id.Reveal());
            name.ShouldNotContain("raw-check", Case.Insensitive);

            var dump = await db.KeyTypeAsync(key) == RedisType.Hash
                ? string.Join("|", (await db.HashGetAllAsync(key)).Select(e => Encoding.Latin1.GetString((byte[])e.Value!)))
                : Encoding.Latin1.GetString((byte[])(await db.StringGetAsync(key))!);
            dump.ShouldNotContain("raw-check", Case.Insensitive);
            dump.ShouldNotContain(issued.Code!.Value.Reveal());
        }
    }

    [Fact]
    public async Task A_missing_challenge_still_pays_one_unprotect()
    {
        // The timing of the missing arm must not stand out against the real one; the pin is on the work, not
        // on a stopwatch: exactly one Unprotect, as the real arm pays.
        var counting = new CountingKeyring(_keyring);
        var store = Store(counting);
        counting.Unprotects = 0;

        await store.ConsumeCodeAsync(ChallengeId.Generate(), LoginCode.FromRaw("123456"), Ct);
        var missingArm = counting.Unprotects;

        var (id, issued) = await PutAsync("pays@example.com", store: store);
        counting.Unprotects = 0;
        await store.ConsumeCodeAsync(id, WrongCodeFor(issued.Code!.Value), Ct);
        var realArm = counting.Unprotects;

        missingArm.ShouldBe(1);
        realArm.ShouldBe(1);
    }

    [Fact]
    public async Task An_unreachable_redis_throws_the_store_unavailable_contract()
    {
        // Its own container: stopping the shared one would fail every test after this in the class.
        var redis = VolatileRedisContainer.FromDeployCompose();
        await redis.StartAsync(Ct);
        try
        {
            using var connection = new VolatileRedisConnection(
                $"{VolatileRedisContainer.OperatorConnectionString(redis)},connectTimeout=1000,syncTimeout=1000");
            var store = new RedisLoginChallengeStore(connection, _keyring, NullLogger<RedisLoginChallengeStore>.Instance);
            await redis.StopAsync(Ct);

            await Should.ThrowAsync<VolatileRedisUnavailableException>(
                () => store.ConsumeCodeAsync(ChallengeId.Generate(), LoginCode.FromRaw("123456"), Ct));
        }
        finally
        {
            await VolatileRedisContainer.DisposeAsync(redis);
        }
    }

    private static byte[] Base64UrlBytes(string raw) => System.Buffers.Text.Base64Url.DecodeFromChars(raw);

    private sealed class CountingKeyring(IDataProtectionProvider inner) : IDataProtectionProvider
    {
        public int Unprotects;

        public IDataProtector CreateProtector(string purpose) => new Counting(inner.CreateProtector(purpose), this);

        private sealed class Counting(IDataProtector inner, CountingKeyring owner) : IDataProtector
        {
            public IDataProtector CreateProtector(string purpose) => new Counting(inner.CreateProtector(purpose), owner);

            public byte[] Protect(byte[] plaintext) => inner.Protect(plaintext);

            public byte[] Unprotect(byte[] protectedData)
            {
                Interlocked.Increment(ref owner.Unprotects);
                return inner.Unprotect(protectedData);
            }
        }
    }
}
