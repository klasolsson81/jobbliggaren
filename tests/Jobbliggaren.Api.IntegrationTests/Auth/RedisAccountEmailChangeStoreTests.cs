using System.Text;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.IntegrationTests.Sessions;
using Jobbliggaren.Application.Auth.AccountEmailChanges;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Auth.AccountEmailChanges;
using Jobbliggaren.TestSupport;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Shouldly;
using StackExchange.Redis;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// #1975 — the contract of the store that holds an address change an administrator starts (ADR 0153), against the
/// deploy stack's own <c>redis-volatile</c>. What is pinned is what keeps the change completable only by whoever holds
/// both the code mailed to the new address and the account's current address, only after the delay and only once, with
/// one change per account and one per new address. The delay, the window and the attempt count are spelled out as
/// literals. Every instant is produced by the clock the store is given, never by an edited record.
/// </summary>
public sealed class RedisAccountEmailChangeStoreTests : IAsyncLifetime, IClassFixture<SharedVolatileRedisFixture>
{
    private const string Current = "nuvarande@example.se";
    private const string NewEmail = "Ny.Adress@example.se";

    private readonly SharedVolatileRedisFixture _redis;
    private readonly IDataProtectionProvider _keyring = new EphemeralDataProtectionProvider();
    private readonly MutableFakeDateTimeProvider _clock = new();
    private readonly RecordingLogger<RedisAccountEmailChangeStore> _logger = new();

    // The test's OWN reader, beside the connection the store is given.
    private ConnectionMultiplexer _mux = null!;
    private VolatileRedisConnection _connection = null!;
    private RedisAccountEmailChangeStore _store = null!;

    public RedisAccountEmailChangeStoreTests(SharedVolatileRedisFixture redis) => _redis = redis;

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

    private RedisAccountEmailChangeStore Store(IDataProtectionProvider keyring) =>
        new(_connection, keyring, _clock, _logger);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string RecordKey(string newEmail) =>
        RedisAccountEmailChangeStore.RecordKey(RedisAccountEmailChangeStore.RecordSegment(newEmail));

    private async Task<AccountEmailChangePut.Written> PutAsync(
        Guid userId, string newEmail = NewEmail, string current = Current) =>
        (await _store.PutAsync(new NewAccountEmailChange(userId, newEmail, current), Ct))
            .ShouldBeOfType<AccountEmailChangePut.Written>();

    private Task<AccountEmailChangeVerdict> ConsumeAsync(
        LoginCode code, string newEmail = NewEmail, string current = Current) =>
        _store.ConsumeAsync(newEmail, current, code, Ct);

    private void SeventyThreeHoursPass() => _clock.UtcNow += TimeSpan.FromHours(73);

    private static LoginCode WrongCodeFor(LoginCode code) =>
        LoginCode.FromRaw(code.Reveal() == "000000" ? "111111" : "000000");

    private IEnumerable<string> Keys(string pattern) =>
        _mux.GetServer(_mux.GetEndPoints().Single()).Keys(pattern: pattern).Select(k => k.ToString());

    [Fact]
    public async Task A_put_mints_a_six_digit_code_completable_after_seventy_two_hours_for_twenty_four()
    {
        var written = await PutAsync(Guid.NewGuid());

        written.Code.Reveal().ShouldMatch("^[0-9]{6}$");
        written.CompletableFrom.ShouldBe(_clock.UtcNow + TimeSpan.FromHours(72), TimeSpan.FromMilliseconds(1));
        written.ExpiresAt.ShouldBe(_clock.UtcNow + TimeSpan.FromHours(96), TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task The_record_and_the_index_live_ninety_six_hours()
    {
        var userId = Guid.NewGuid();
        await PutAsync(userId);
        var db = _mux.GetDatabase();

        foreach (var key in new[] { RecordKey(NewEmail), RedisAccountEmailChangeStore.IndexKey(userId) })
        {
            var ttl = (await db.KeyTimeToLiveAsync(key)).ShouldNotBeNull();
            ttl.ShouldBeLessThanOrEqualTo(TimeSpan.FromHours(96));
            ttl.ShouldBeGreaterThan(TimeSpan.FromHours(95));
        }
    }

    [Fact]
    public async Task A_full_match_after_the_delay_verifies_once_and_carries_the_records_spelling_and_where_it_started()
    {
        var userId = Guid.NewGuid();
        var written = await PutAsync(userId);
        SeventyThreeHoursPass();

        var first = await ConsumeAsync(written.Code);
        var second = await ConsumeAsync(written.Code);

        var proof = first.ShouldBeOfType<AccountEmailChangeVerdict.Verified>().Proof;
        proof.UserId.ShouldBe(userId);
        proof.NewEmail.ShouldBe(NewEmail);
        proof.ExpectedCurrent.Fingerprint.ShouldBe(SubjectFingerprint.Hex(Current));
        second.ShouldBe(AccountEmailChangeVerdict.Unusable.Instance);
        Keys("jobbliggaren:auth/account-email-change/v2/*").ShouldBeEmpty();
    }

    [Fact]
    public async Task A_full_match_before_the_delay_answers_not_yet_and_spends_no_attempt()
    {
        var written = await PutAsync(Guid.NewGuid());

        for (var i = 0; i < 4; i++)
        {
            (await ConsumeAsync(written.Code)).ShouldBe(
                new AccountEmailChangeVerdict.NotYet(written.CompletableFrom));
        }

        SeventyThreeHoursPass();
        (await ConsumeAsync(written.Code)).ShouldBeOfType<AccountEmailChangeVerdict.Verified>();
    }

    [Fact]
    public async Task The_delay_holds_until_its_own_instant_and_no_longer()
    {
        var written = await PutAsync(Guid.NewGuid());

        _clock.UtcNow = written.CompletableFrom - TimeSpan.FromSeconds(1);
        (await ConsumeAsync(written.Code)).ShouldBeOfType<AccountEmailChangeVerdict.NotYet>();

        _clock.UtcNow = written.CompletableFrom;
        (await ConsumeAsync(written.Code)).ShouldBeOfType<AccountEmailChangeVerdict.Verified>();
    }

    [Fact]
    public async Task A_full_match_after_the_usable_window_is_unusable_and_consumes_the_change()
    {
        // Redis removes the record at its TTL; the window is still enforced from inside the payload, by the clock.
        var written = await PutAsync(Guid.NewGuid());
        _clock.UtcNow = written.ExpiresAt;

        (await ConsumeAsync(written.Code)).ShouldBe(AccountEmailChangeVerdict.Unusable.Instance);
        Keys("jobbliggaren:auth/account-email-change/v2/*").ShouldBeEmpty();
    }

    [Fact]
    public async Task Before_the_delay_a_wrong_code_is_answered_as_unusable_never_as_not_yet()
    {
        var written = await PutAsync(Guid.NewGuid());

        (await ConsumeAsync(WrongCodeFor(written.Code))).ShouldBe(AccountEmailChangeVerdict.Unusable.Instance);
        (await ConsumeAsync(written.Code, current: "fel@example.se"))
            .ShouldBe(AccountEmailChangeVerdict.Unusable.Instance);
    }

    [Fact]
    public async Task Two_misses_then_a_full_match_on_the_last_attempt_verifies()
    {
        var written = await PutAsync(Guid.NewGuid());
        SeventyThreeHoursPass();

        (await ConsumeAsync(WrongCodeFor(written.Code))).ShouldBe(AccountEmailChangeVerdict.Unusable.Instance);
        (await ConsumeAsync(written.Code, current: "fel@example.se"))
            .ShouldBe(AccountEmailChangeVerdict.Unusable.Instance);

        (await ConsumeAsync(written.Code)).ShouldBeOfType<AccountEmailChangeVerdict.Verified>();
    }

    [Fact]
    public async Task Three_wrong_current_addresses_burn_the_change_and_the_right_one_stays_refused()
    {
        // Whoever holds the code but not the account's address spends this change's attempts guessing it.
        var userId = Guid.NewGuid();
        var written = await PutAsync(userId);
        SeventyThreeHoursPass();

        foreach (var guess in new[] { "a@example.se", "b@example.se", "c@example.se" })
            (await ConsumeAsync(written.Code, current: guess)).ShouldBe(AccountEmailChangeVerdict.Unusable.Instance);

        (await ConsumeAsync(written.Code)).ShouldBe(AccountEmailChangeVerdict.Unusable.Instance);
        (await _store.FindPendingAsync(userId, Ct)).ShouldNotBeNull().State
            .ShouldBe(PendingAccountEmailChangeState.CodeBurned);
    }

    [Fact]
    public async Task The_burn_is_logged_once_with_the_target_user_and_nothing_presented()
    {
        var userId = Guid.NewGuid();
        var written = await PutAsync(userId);
        var wrong = WrongCodeFor(written.Code);

        for (var i = 0; i < 4; i++)
            await ConsumeAsync(wrong);

        var burned = _logger.Records.ShouldHaveSingleItem();
        burned.Level.ShouldBe(LogLevel.Warning);
        burned.Properties.ShouldContain(p => p.Key == "TargetUserId" && Equals(p.Value, userId));
        foreach (var secret in new[] { NewEmail, Current, written.Code.Reveal(), wrong.Reveal() })
            burned.Message.ShouldNotContain(secret, Case.Insensitive);
    }

    [Fact]
    public async Task Every_spelling_identity_folds_together_reaches_the_one_change()
    {
        const string newEmail = "Björn.Ny@example.se";
        var written = await PutAsync(Guid.NewGuid(), newEmail: newEmail, current: "Åsa@example.se");
        SeventyThreeHoursPass();

        // Case, surrounding whitespace and a decomposed (NFD) spelling, on both addresses.
        var verdict = await ConsumeAsync(
            written.Code,
            newEmail: "  " + "BJÖRN.NY@example.SE".Normalize(NormalizationForm.FormD),
            current: "åsa@EXAMPLE.se".Normalize(NormalizationForm.FormD) + " ");

        verdict.ShouldBeOfType<AccountEmailChangeVerdict.Verified>().Proof.NewEmail.ShouldBe(newEmail);
    }

    [Fact]
    public async Task A_change_that_does_not_exist_is_unusable_and_leaves_no_key_behind()
    {
        (await ConsumeAsync(LoginCode.FromRaw("123456"), newEmail: "ingen@example.se"))
            .ShouldBe(AccountEmailChangeVerdict.Unusable.Instance);

        Keys("jobbliggaren:auth/account-email-change*").ShouldBeEmpty();
    }

    [Fact]
    public async Task A_change_whose_ttl_has_run_out_is_gone()
    {
        var userId = Guid.NewGuid();
        var written = await PutAsync(userId);

        // The actor is the TTL, shortened here rather than waited out: nothing deletes the key by hand.
        await _mux.GetDatabase().KeyExpireAsync(RecordKey(NewEmail), TimeSpan.FromMilliseconds(1));
        await Task.Delay(50, Ct);
        SeventyThreeHoursPass();

        (await ConsumeAsync(written.Code)).ShouldBe(AccountEmailChangeVerdict.Unusable.Instance);
        (await _store.FindPendingAsync(userId, Ct)).ShouldBeNull();
        (await _store.CancelAsync(userId, Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_second_put_for_the_account_displaces_the_first_whose_code_no_longer_completes()
    {
        var userId = Guid.NewGuid();
        var first = await PutAsync(userId, newEmail: "forsta@example.se");
        var second = await PutAsync(userId, newEmail: "andra@example.se");
        SeventyThreeHoursPass();

        (await ConsumeAsync(first.Code, newEmail: "forsta@example.se"))
            .ShouldBe(AccountEmailChangeVerdict.Unusable.Instance);
        (await ConsumeAsync(second.Code, newEmail: "andra@example.se"))
            .ShouldBeOfType<AccountEmailChangeVerdict.Verified>();
    }

    [Fact]
    public async Task A_put_to_the_same_address_for_the_same_account_replaces_the_code_and_keeps_one_record()
    {
        var userId = Guid.NewGuid();
        var first = await PutAsync(userId);
        var second = await PutAsync(userId);
        SeventyThreeHoursPass();

        Keys("jobbliggaren:auth/account-email-change/v2/*").ShouldHaveSingleItem();
        if (first.Code.Reveal() != second.Code.Reveal())
            (await ConsumeAsync(first.Code)).ShouldBe(AccountEmailChangeVerdict.Unusable.Instance);

        (await ConsumeAsync(second.Code)).ShouldBeOfType<AccountEmailChangeVerdict.Verified>();
    }

    [Fact]
    public async Task A_put_for_an_address_another_accounts_change_holds_is_refused_and_displaces_nothing()
    {
        var holder = Guid.NewGuid();
        var other = Guid.NewGuid();
        var held = await PutAsync(holder);
        var othersOwn = await PutAsync(other, newEmail: "egen@example.se");

        var refused = await _store.PutAsync(new NewAccountEmailChange(other, NewEmail, "annan@example.se"), Ct);
        SeventyThreeHoursPass();

        refused.ShouldBe(AccountEmailChangePut.AddressPendingForAnotherAccount.Instance);
        (await ConsumeAsync(held.Code)).ShouldBeOfType<AccountEmailChangeVerdict.Verified>().Proof.UserId.ShouldBe(holder);
        (await ConsumeAsync(othersOwn.Code, newEmail: "egen@example.se"))
            .ShouldBeOfType<AccountEmailChangeVerdict.Verified>();
    }

    [Fact]
    public async Task A_revoke_removes_the_record_its_put_wrote_and_never_a_newer_one_to_the_same_address()
    {
        var userId = Guid.NewGuid();
        var first = await PutAsync(userId);
        var second = await PutAsync(userId);

        await _store.RevokeAsync(first.Receipt, Ct);
        SeventyThreeHoursPass();
        (await ConsumeAsync(second.Code)).ShouldBeOfType<AccountEmailChangeVerdict.Verified>();

        var third = await PutAsync(userId);
        await _store.RevokeAsync(third.Receipt, Ct);
        (await ConsumeAsync(third.Code)).ShouldBe(AccountEmailChangeVerdict.Unusable.Instance);
        (await _store.FindPendingAsync(userId, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task The_pending_read_carries_the_two_instants_and_no_address()
    {
        var userId = Guid.NewGuid();
        var request = new NewAccountEmailChange(userId, NewEmail, Current);
        var issuedAt = _clock.UtcNow;
        var written = (await _store.PutAsync(request, Ct)).ShouldBeOfType<AccountEmailChangePut.Written>();

        var pending = (await _store.FindPendingAsync(userId, Ct)).ShouldNotBeNull();

        pending.ShouldBe(new PendingAccountEmailChange(
            PendingAccountEmailChangeState.Pending, written.CompletableFrom, written.ExpiresAt)
        {
            RequestId = request.RequestId,
            IssuedAt = issuedAt,
        });
        (await _store.FindPendingAsync(Guid.NewGuid(), Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task A_cancel_removes_the_pending_change_once()
    {
        var userId = Guid.NewGuid();
        var written = await PutAsync(userId);

        (await _store.CancelAsync(userId, Ct)).ShouldBeTrue();
        (await _store.CancelAsync(userId, Ct)).ShouldBeFalse();
        SeventyThreeHoursPass();

        (await ConsumeAsync(written.Code)).ShouldBe(AccountEmailChangeVerdict.Unusable.Instance);
        (await _store.FindPendingAsync(userId, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task A_stale_pointer_reaches_neither_the_read_nor_the_cancel_of_another_accounts_change_at_its_address()
    {
        // The index is never deleted: after A's change is cancelled it still names the address, which B's change
        // then takes over.
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await PutAsync(a);
        (await _store.CancelAsync(a, Ct)).ShouldBeTrue();
        var bs = await PutAsync(b, current: "b@example.se");

        (await _store.FindPendingAsync(a, Ct)).ShouldBeNull();
        (await _store.CancelAsync(a, Ct)).ShouldBeFalse();
        await PutAsync(a, newEmail: "a-igen@example.se");
        SeventyThreeHoursPass();

        (await ConsumeAsync(bs.Code, current: "b@example.se")).ShouldBeOfType<AccountEmailChangeVerdict.Verified>()
            .Proof.UserId.ShouldBe(b);
    }

    [Fact]
    public async Task A_change_the_accounts_pointer_no_longer_names_cannot_complete()
    {
        // The state a Redis fault leaves between a put's index swap and its removal of the record it displaced: the
        // displaced record is still there and the index names the newer one. Reproduced by restoring the displaced
        // record the second put removed, since a fault cannot be injected into the store.
        var userId = Guid.NewGuid();
        var first = await PutAsync(userId, newEmail: "forsta@example.se");
        var db = _mux.GetDatabase();
        var displacedKey = RecordKey("forsta@example.se");
        var dump = (await db.KeyDumpAsync(displacedKey)).ShouldNotBeNull();
        await PutAsync(userId, newEmail: "andra@example.se");
        await db.KeyRestoreAsync(displacedKey, dump, TimeSpan.FromHours(96));
        SeventyThreeHoursPass();

        (await ConsumeAsync(first.Code, newEmail: "forsta@example.se"))
            .ShouldBe(AccountEmailChangeVerdict.Unusable.Instance);
        (await db.KeyExistsAsync(displacedKey)).ShouldBeFalse();
    }

    [Fact]
    public async Task Two_concurrent_completions_verify_exactly_once()
    {
        var written = await PutAsync(Guid.NewGuid());
        SeventyThreeHoursPass();

        var verdicts = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => ConsumeAsync(written.Code)));

        verdicts.Count(v => v is AccountEmailChangeVerdict.Verified).ShouldBe(1);
    }

    [Fact]
    public async Task A_completion_racing_a_cancel_lets_exactly_one_of_them_through()
    {
        var userId = Guid.NewGuid();
        var written = await PutAsync(userId);
        SeventyThreeHoursPass();

        var completion = ConsumeAsync(written.Code);
        var cancel = _store.CancelAsync(userId, Ct);
        await Task.WhenAll(completion, cancel);

        ((await completion) is AccountEmailChangeVerdict.Verified).ShouldNotBe(await cancel);
    }

    [Fact]
    public async Task A_change_written_under_a_lost_keyring_is_unusable()
    {
        var written = await PutAsync(Guid.NewGuid());
        SeventyThreeHoursPass();

        var afterKeyLoss = Store(new EphemeralDataProtectionProvider());

        (await afterKeyLoss.ConsumeAsync(NewEmail, Current, written.Code, Ct))
            .ShouldBe(AccountEmailChangeVerdict.Unusable.Instance);
    }

    [Fact]
    public async Task Redis_holds_neither_address_the_code_nor_the_user_id()
    {
        var userId = Guid.NewGuid();
        var written = await PutAsync(userId);
        var db = _mux.GetDatabase();

        var keys = Keys("jobbliggaren:auth/account-email-change*").ToList();
        var fields = await db.HashGetAllAsync(RecordKey(NewEmail));
        var stored = string.Join("|", fields.Select(f =>
            f.Name + "=" + Encoding.Latin1.GetString((byte[])f.Value!)));
        var payload = Encoding.Latin1.GetString((byte[])fields.Single(f => f.Name == "p").Value!);

        keys.Count.ShouldBe(2);
        foreach (var secret in new[] { NewEmail, Current, userId.ToString("D"), userId.ToString("N") })
        {
            keys.ShouldAllBe(k => !k.Contains(secret, StringComparison.OrdinalIgnoreCase));
            stored.ShouldNotContain(secret, Case.Insensitive);
        }

        // Only the payload: the plain instants are thirteen digits, in which six random ones can occur.
        payload.ShouldNotContain(written.Code.Reveal());
        stored.ShouldNotContain(SubjectFingerprint.Hex(Current));
    }
}
