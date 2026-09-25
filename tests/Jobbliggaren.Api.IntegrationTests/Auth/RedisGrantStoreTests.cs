using System.Text;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Application.Auth.Grants;
using Jobbliggaren.Application.Common.Validation;
using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Auth.Grants;
using Jobbliggaren.TestSupport;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using StackExchange.Redis;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// The grant store's contract against a real Redis (ADR 0142 D3): single use is <c>GETDEL</c>, the lifetime is
/// the key's TTL, and a Redis reader learns neither the token nor the address. Measured on the adapter
/// production registers, on the deploy stack's own <c>redis-volatile</c>. The lifetime is spelled out as a
/// literal 10, so a change to the policy constant makes this go red.
/// </summary>
public sealed class RedisGrantStoreTests : IAsyncLifetime, IClassFixture<SharedVolatileRedisFixture>
{
    private readonly SharedVolatileRedisFixture _redis;
    private readonly EphemeralDataProtectionProvider _keyring = new();

    // The test's OWN reader, beside the connection the store is given.
    private ConnectionMultiplexer _mux = null!;
    private VolatileRedisConnection _connection = null!;
    private RedisGrantStore _store = null!;

    public RedisGrantStoreTests(SharedVolatileRedisFixture redis) => _redis = redis;

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
    /// <c>src/</c> writes one, so the record is written by hand, under the key and the protector the adapter
    /// uses, and the test asserts only that the read side degrades to "no grant".
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

    private Task WriteByHandAsync(GrantToken token, int purpose, string email) =>
        WriteByHandAsync(token, subPurpose: "1", new { p = purpose, e = email });

    private async Task WriteByHandAsync(GrantToken token, string subPurpose, object record)
    {
        var payload = _keyring
            .CreateProtector(RedisGrantStore.ProtectorPurpose)
            .CreateProtector(subPurpose)
            .Protect(JsonSerializer.SerializeToUtf8Bytes(record));
        await _mux.GetDatabase().StringSetAsync(RedisGrantStore.Key(token), payload, TimeSpan.FromMinutes(10));
    }

    /// <summary>
    /// UNREACHABLE STATES, declared: a record of a caller-asserted purpose that lacks the field its binding is
    /// made of, or carries the empty user id. No path in <c>src/</c> writes one (the adapter serialises the whole
    /// subject), so each is written by hand under its purpose's own protector, and the test asserts only that
    /// the read side degrades to "no grant" even for a caller asserting exactly that degenerate binding.
    /// </summary>
    [Fact]
    public async Task A_caller_asserted_record_without_its_binding_redeems_to_nothing()
    {
        var userId = Guid.NewGuid();
        var withoutUser = GrantToken.Generate();
        var withTheEmptyUser = GrantToken.Generate();
        var withoutAddress = GrantToken.Generate();
        var control = GrantToken.Generate();
        await WriteByHandAsync(withoutUser, subPurpose: "2", new { p = 2 });
        await WriteByHandAsync(withTheEmptyUser, subPurpose: "2", new { p = 2, u = Guid.Empty });
        await WriteByHandAsync(withoutAddress, subPurpose: "3", new { p = 3, u = userId });
        await WriteByHandAsync(control, subPurpose: "3", new { p = 3, u = userId, e = "ny@example.se" });

        var emptyUser = GrantAssertion.Of(new GrantSubject.Reauthentication(Guid.Empty));
        (await _store.RedeemAsync(withoutUser, emptyUser, Ct)).ShouldBeNull();
        (await _store.RedeemAsync(withTheEmptyUser, emptyUser, Ct)).ShouldBeNull();
        (await _store.RedeemAsync(
            withoutAddress, GrantAssertion.Of(new GrantSubject.ChangeEmail(userId, string.Empty)), Ct)).ShouldBeNull();
        (await _store.RedeemAsync(control, GrantAssertion.Of(new GrantSubject.ChangeEmail(userId, "ny@example.se")), Ct))
            .ShouldBe(new GrantSubject.ChangeEmail(userId, "ny@example.se"));
    }

    // ── #1739: the two caller-asserted purposes (ADR 0142 D5) ──────────────────────────────────────────────

    [Fact]
    public async Task A_reauthentication_grant_is_redeemed_by_the_user_it_was_issued_to()
    {
        var subject = new GrantSubject.Reauthentication(Guid.NewGuid());
        var token = await _store.IssueAsync(subject, Ct);

        (await _store.RedeemAsync(token, GrantAssertion.Of(subject), Ct)).ShouldBe(subject);
        (await _store.RedeemAsync(token, GrantAssertion.Of(subject), Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task A_change_email_grant_is_redeemed_for_the_user_and_the_address_it_was_issued_for()
    {
        var subject = new GrantSubject.ChangeEmail(Guid.NewGuid(), "ny@example.se");
        var token = await _store.IssueAsync(subject, Ct);

        (await _store.RedeemAsync(token, GrantAssertion.Of(subject), Ct)).ShouldBe(subject);
    }

    [Fact]
    public async Task A_grant_issued_to_one_user_is_refused_for_another_and_is_spent_by_the_attempt()
    {
        var owner = new GrantSubject.Reauthentication(Guid.NewGuid());
        var token = await _store.IssueAsync(owner, Ct);

        var asAnother = await _store.RedeemAsync(
            token, GrantAssertion.Of(new GrantSubject.Reauthentication(Guid.NewGuid())), Ct);
        var asTheOwnerAfterwards = await _store.RedeemAsync(token, GrantAssertion.Of(owner), Ct);

        asAnother.ShouldBeNull();

        // GETDEL runs before the compare, so the refused attempt took the grant with it.
        asTheOwnerAfterwards.ShouldBeNull();
    }

    [Fact]
    public async Task A_grant_issued_for_one_purpose_is_refused_for_every_other()
    {
        var userId = Guid.NewGuid();
        var reauthentication = await _store.IssueAsync(new GrantSubject.Reauthentication(userId), Ct);
        var changeEmail = await _store.IssueAsync(new GrantSubject.ChangeEmail(userId, "ny@example.se"), Ct);
        var loginComplete = await _store.IssueAsync(new GrantSubject.LoginComplete("ny@example.se"), Ct);

        (await _store.RedeemAsync(
            reauthentication, GrantAssertion.Of(new GrantSubject.ChangeEmail(userId, "ny@example.se")), Ct)).ShouldBeNull();
        (await _store.RedeemAsync(
            changeEmail, GrantAssertion.Of(new GrantSubject.Reauthentication(userId)), Ct)).ShouldBeNull();
        (await _store.RedeemAsync(
            loginComplete, GrantAssertion.Of(new GrantSubject.Reauthentication(userId)), Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task A_change_email_grant_is_refused_for_another_address_and_for_another_user()
    {
        var userId = Guid.NewGuid();
        var forAnotherAddress = await _store.IssueAsync(new GrantSubject.ChangeEmail(userId, "visad@example.se"), Ct);
        var forAnotherUser = await _store.IssueAsync(new GrantSubject.ChangeEmail(userId, "visad@example.se"), Ct);

        (await _store.RedeemAsync(
            forAnotherAddress, GrantAssertion.Of(new GrantSubject.ChangeEmail(userId, "annan@example.se")), Ct)).ShouldBeNull();
        (await _store.RedeemAsync(
            forAnotherUser, GrantAssertion.Of(new GrantSubject.ChangeEmail(Guid.NewGuid(), "visad@example.se")), Ct)).ShouldBeNull();
    }

    // Derived from the enum, so a purpose added without a row here fails loud instead of dropping out (#1744,
    // test-writer Major 10).
    public static TheoryData<GrantPurpose> EveryPurpose() => new(Enum.GetValues<GrantPurpose>());

    private static GrantSubject SubjectFor(GrantPurpose purpose, string email = "ttl@example.se") => purpose switch
    {
        GrantPurpose.LoginComplete => new GrantSubject.LoginComplete(email),
        GrantPurpose.Reauthentication => new GrantSubject.Reauthentication(Guid.NewGuid()),
        GrantPurpose.ChangeEmail => new GrantSubject.ChangeEmail(Guid.NewGuid(), email),
        GrantPurpose.LoginCompleteExternal => new GrantSubject.LoginCompleteExternal(
            email, ExternalProviderKey.Google, ExternalSubject.TryCreate("110248495921238986420")!.Value),
        _ => throw new InvalidOperationException($"No subject row for grant purpose {purpose}."),
    };

    [Theory]
    [MemberData(nameof(EveryPurpose))]
    public async Task Every_purpose_lives_the_same_ten_minutes(GrantPurpose purpose)
    {
        var token = await _store.IssueAsync(SubjectFor(purpose), Ct);

        var ttl = await _mux.GetDatabase().KeyTimeToLiveAsync(RedisGrantStore.Key(token));

        ttl.ShouldNotBeNull().ShouldBeLessThanOrEqualTo(TimeSpan.FromMinutes(10));
        ttl.Value.ShouldBeGreaterThan(TimeSpan.FromMinutes(9));
    }

    [Fact]
    public async Task Redis_holds_neither_the_user_id_nor_the_new_address()
    {
        var userId = Guid.NewGuid();
        const string newEmail = "reader-ny@example.se";
        var token = await _store.IssueAsync(new GrantSubject.ChangeEmail(userId, newEmail), Ct);

        var stored = Encoding.Latin1.GetString(
            (byte[])(await _mux.GetDatabase().StringGetAsync(RedisGrantStore.Key(token)))!);

        stored.ShouldNotContain(newEmail);
        stored.ShouldNotContain(userId.ToString("D"));
        stored.ShouldNotContain(userId.ToString("N"));
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

    [Fact]
    public async Task The_stored_length_is_the_same_for_every_purpose_and_every_address()
    {
        // #1739 (security-auditor's Minor 1 on #1793): the three purposes share one key family, so an unpadded
        // value would tell a Redis reader which purpose a grant was issued for, and how long its address is.
        // The ceiling is one constant for every payload — the longest address a validator admits, every
        // character escaped — so a 256-character address and a re-authentication grant with none protect to
        // one length. The DataProtector adds a fixed envelope, so the protected lengths compare directly.
        var longest = $"{new string('a', EmailAddressRules.MaximumLength - "@example.se".Length)}@example.se";
        longest.Length.ShouldBe(EmailAddressRules.MaximumLength);
        // #1744: the external purpose at both ends too — the longest address with the longest subject OIDC allows.
        var longestSubject = ExternalSubject.TryCreate(new string('7', ExternalSubject.MaximumLength))!.Value;
        var shortSubject = ExternalSubject.TryCreate("1")!.Value;
        GrantSubject[] subjects =
        [
            new GrantSubject.LoginComplete("a@b.se"),
            new GrantSubject.LoginComplete(longest),
            new GrantSubject.Reauthentication(Guid.NewGuid()),
            new GrantSubject.ChangeEmail(Guid.NewGuid(), "a@b.se"),
            new GrantSubject.ChangeEmail(Guid.NewGuid(), longest),
            new GrantSubject.LoginCompleteExternal("a@b.se", ExternalProviderKey.Google, shortSubject),
            new GrantSubject.LoginCompleteExternal(longest, ExternalProviderKey.Google, longestSubject),
        ];
        var db = _mux.GetDatabase();

        var lengths = new List<long>();
        foreach (var subject in subjects)
        {
            var token = await _store.IssueAsync(subject, Ct);
            lengths.Add(await db.StringLengthAsync(RedisGrantStore.Key(token)));
        }

        lengths.Distinct().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_padded_grant_still_redeems_to_its_subject()
    {
        var userId = Guid.NewGuid();
        var subject = new GrantSubject.ChangeEmail(
            userId, $"{new string('a', EmailAddressRules.MaximumLength - "@example.se".Length)}@example.se");
        var token = await _store.IssueAsync(subject, Ct);

        (await _store.RedeemAsync(token, GrantAssertion.Of(subject), Ct)).ShouldBe(subject);
    }

    // ── #1744: the external registration purpose (ADR 0142 D3, D8) ────────────────────────────────────────────

    private static GrantAssertion EitherRegistration =>
        GrantAssertion.Bearer(GrantPurpose.LoginComplete, GrantPurpose.LoginCompleteExternal);

    private static GrantSubject.LoginCompleteExternal ExternalSubjectFor(string email) =>
        new(email, ExternalProviderKey.Google, ExternalSubject.TryCreate("110248495921238986420")!.Value);

    [Fact]
    public async Task An_external_grant_is_redeemed_once_by_a_caller_that_accepts_either_registration()
    {
        var subject = ExternalSubjectFor("extern@example.se");
        var token = await _store.IssueAsync(subject, Ct);

        (await _store.RedeemAsync(token, EitherRegistration, Ct)).ShouldBe(subject);
        (await _store.RedeemAsync(token, EitherRegistration, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task A_code_grant_is_still_redeemed_by_a_caller_that_accepts_either_registration()
    {
        var token = await _store.IssueAsync(new GrantSubject.LoginComplete("kod@example.se"), Ct);

        (await _store.RedeemAsync(token, EitherRegistration, Ct)).ShouldBe(new GrantSubject.LoginComplete("kod@example.se"));
    }

    [Fact]
    public async Task An_external_grant_redeemed_as_a_code_grant_is_refused_and_spent_by_the_attempt()
    {
        var external = await _store.IssueAsync(ExternalSubjectFor("korsad@example.se"), Ct);
        var code = await _store.IssueAsync(new GrantSubject.LoginComplete("korsad@example.se"), Ct);

        (await _store.RedeemAsync(external, GrantAssertion.Bearer(GrantPurpose.LoginComplete), Ct)).ShouldBeNull();
        (await _store.RedeemAsync(code, GrantAssertion.Bearer(GrantPurpose.LoginCompleteExternal), Ct)).ShouldBeNull();

        // One GETDEL per redemption, before any protector is tried: the refused attempts took both grants.
        (await _store.RedeemAsync(external, EitherRegistration, Ct)).ShouldBeNull();
        (await _store.RedeemAsync(code, EitherRegistration, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Redis_holds_neither_the_subject_nor_the_address_of_an_external_grant()
    {
        const string email = "extern-reader@example.se";
        var token = await _store.IssueAsync(ExternalSubjectFor(email), Ct);

        var stored = Encoding.Latin1.GetString(
            (byte[])(await _mux.GetDatabase().StringGetAsync(RedisGrantStore.Key(token)))!);

        stored.ShouldNotContain(email);
        stored.ShouldNotContain("110248495921238986420");
    }

    /// <summary>
    /// A grant written by the build before #1744: purpose 1, the JSON that build serialised (the three members, the
    /// user id as null) padded with spaces to the ceiling it used. Its bytes are written by hand under the
    /// production protector, because the actor that wrote them — the pre-#1744 <c>RedisGrantStore</c> — is retired;
    /// <see cref="Purposes_one_to_three_serialise_exactly_as_before_the_external_members"/> pins that the current
    /// writer still produces that shape. A grant alive across the deploy must still redeem.
    /// </summary>
    [Fact]
    public async Task A_grant_written_before_the_external_purpose_existed_still_redeems()
    {
        var token = GrantToken.Generate();
        var legacy = Encoding.UTF8.GetBytes("""{"p":1,"e":"fore@example.se","u":null}""" + new string(' ', 400));
        var payload = _keyring
            .CreateProtector(RedisGrantStore.ProtectorPurpose)
            .CreateProtector("1")
            .Protect(legacy);
        await _mux.GetDatabase().StringSetAsync(RedisGrantStore.Key(token), payload, TimeSpan.FromMinutes(10));

        (await _store.RedeemAsync(token, EitherRegistration, Ct)).ShouldBe(new GrantSubject.LoginComplete("fore@example.se"));
    }

    [Fact]
    public void Purposes_one_to_three_serialise_exactly_as_before_the_external_members()
    {
        // The two external members are omitted when null (ADR 0142 D1: no live record changes shape). One row per
        // purpose, in the shape each one's subject serialises to.
        var userId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        static string Serialised(RedisGrantStore.GrantPayload payload) =>
            Encoding.UTF8.GetString(JsonSerializer.SerializeToUtf8Bytes(payload));

        Serialised(new RedisGrantStore.GrantPayload(1, "a@b.se", UserId: null))
            .ShouldBe("""{"p":1,"e":"a@b.se","u":null}""");
        Serialised(new RedisGrantStore.GrantPayload(2, Email: null, userId))
            .ShouldBe("""{"p":2,"e":null,"u":"11111111-2222-3333-4444-555555555555"}""");
        Serialised(new RedisGrantStore.GrantPayload(3, "a@b.se", userId))
            .ShouldBe("""{"p":3,"e":"a@b.se","u":"11111111-2222-3333-4444-555555555555"}""");
    }

    /// <summary>
    /// UNREACHABLE STATES, declared: an external record without its provider, with an unknown provider, or without a
    /// usable subject. The adapter serialises the whole subject, so no path in <c>src/</c> writes one; each is
    /// written by hand under purpose 4's protector, and the test asserts only that the read side refuses it.
    /// </summary>
    [Fact]
    public async Task An_external_record_without_a_usable_provider_or_subject_redeems_to_nothing()
    {
        var withoutProvider = GrantToken.Generate();
        var unknownProvider = GrantToken.Generate();
        var withoutSubject = GrantToken.Generate();
        var control = GrantToken.Generate();
        await WriteByHandAsync(withoutProvider, subPurpose: "4", new { p = 4, e = "a@b.se", s = "1" });
        await WriteByHandAsync(unknownProvider, subPurpose: "4", new { p = 4, e = "a@b.se", pr = "myspace", s = "1" });
        await WriteByHandAsync(withoutSubject, subPurpose: "4", new { p = 4, e = "a@b.se", pr = "google", s = "" });
        await WriteByHandAsync(control, subPurpose: "4", new { p = 4, e = "a@b.se", pr = "google", s = "1" });

        (await _store.RedeemAsync(withoutProvider, EitherRegistration, Ct)).ShouldBeNull();
        (await _store.RedeemAsync(unknownProvider, EitherRegistration, Ct)).ShouldBeNull();
        (await _store.RedeemAsync(withoutSubject, EitherRegistration, Ct)).ShouldBeNull();
        (await _store.RedeemAsync(control, EitherRegistration, Ct)).ShouldBe(
            new GrantSubject.LoginCompleteExternal(
                "a@b.se", ExternalProviderKey.Google, ExternalSubject.TryCreate("1")!.Value));
    }

    [Fact]
    public async Task The_unreadable_payload_line_is_written_only_when_no_asserted_purpose_opens_it()
    {
        var logger = new RecordingLogger<RedisGrantStore>();
        var store = new RedisGrantStore(_connection, _keyring, logger);

        // Purpose 1 is tried after purpose 4 fails to open it: no line for the protector that did not fit.
        var opened = await store.IssueAsync(new GrantSubject.LoginComplete("rad@example.se"), Ct);
        (await store.RedeemAsync(
            opened, GrantAssertion.Bearer(GrantPurpose.LoginCompleteExternal, GrantPurpose.LoginComplete), Ct))
            .ShouldNotBeNull();
        logger.Records.ShouldBeEmpty();

        // A lost keyring: no asserted protector opens it, and it is logged once.
        var lost = await store.IssueAsync(new GrantSubject.LoginComplete("rad@example.se"), Ct);
        var afterKeyLoss = new RedisGrantStore(_connection, new EphemeralDataProtectionProvider(), logger);
        (await afterKeyLoss.RedeemAsync(lost, EitherRegistration, Ct)).ShouldBeNull();
        logger.Records.Count(r => r.EventId.Id == 1017).ShouldBe(1);
    }
}
