using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Auth.AccountEmailChanges;
using Jobbliggaren.Application.Auth.Commands.ConsumeLoginLink;
using Jobbliggaren.Application.Auth.Commands.VerifyLoginChallenge;
using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Application.Auth.Grants;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Auth.Access;
using Jobbliggaren.Infrastructure.Auth.Sessions;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// #1735 — the proof half of a login challenge: the two handlers, the one outcome function they share, and
/// the one passwordless session grant (ADR 0142 D3/D4; security-auditor Q21/Q-S3). The store's verdicts are
/// stubbed with the factories <c>RedisLoginChallengeStore</c> answers through, and the profiles are made by
/// <c>JobSeeker.Register</c> and <c>JobSeeker.SoftDelete</c>.
/// </summary>
public sealed class LoginProofTests
{
    private const string Email = "person@example.com";

    private static readonly DateTimeOffset Now = FakeDateTimeProvider.Default.UtcNow;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly ILoginChallengeStore _store = Substitute.For<ILoginChallengeStore>();
    private readonly ILoginAccountLookup _lookup = Substitute.For<ILoginAccountLookup>();
    private readonly IInboxProofRecorder _inbox = Substitute.For<IInboxProofRecorder>();
    private readonly ISessionStore _sessions = Substitute.For<ISessionStore>();
    private readonly IAuthAuditLogger _audit = Substitute.For<IAuthAuditLogger>();
    private readonly IGrantStore _grants = Substitute.For<IGrantStore>();
    private readonly AppDbContext _db = TestAppDbContextFactory.Create();
    private readonly CapturingLogger<LoginProofOutcome> _outcomeLog = new();
    private readonly IExternalLoginLookup _externalLookup = Substitute.For<IExternalLoginLookup>();
    private readonly IExternalLoginWriter _externalWriter = Substitute.For<IExternalLoginWriter>();
    private string _accountEmail = Email;
    private long _accessRevision;
    private long _credentialCutoff;
    private bool _inboxConfirmed = true;
    private readonly AccountAccessTestKit.RecordingAccountAccessCoordinator _coordinator = AccountAccessTestKit.Coordinator();
    private readonly IAccountEmailChangeStore _addressChanges = Substitute.For<IAccountEmailChangeStore>();
    private readonly RecordingLogger<AccountAccessCleanup> _cleanupLogger = new();

    private static readonly GrantToken IssuedGrant = GrantToken.FromRaw("AAECAwQFBgcICQoLDA0ODw");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public LoginProofTests()
    {
        _lookup.FindAccountAsync(Email, Arg.Any<CancellationToken>()).Returns(new LoginAccount(_userId, Email));
        _inbox.RecordAsync(_userId, Arg.Any<CancellationToken>()).Returns(InboxProof.AlreadyConfirmed);
        _sessions.CreateAsync(_userId, Arg.Any<AccountAccessProof>(), Arg.Any<SessionLifetime>(), Arg.Any<CancellationToken>())
            .Returns(call => new Session(
                SessionId.FromRaw("granted-session-id"), _userId, Now, Now.AddDays(30), call.Arg<SessionLifetime>()));
        _sessions.CreateCommittedAsync(Arg.Any<CommittedSessionAuthorization>(), Arg.Any<SessionLifetime>(), Arg.Any<CancellationToken>())
            .Returns(call => new Session(SessionId.FromRaw("granted-session-id"), _userId, Now, Now.AddDays(30),
                call.Arg<SessionLifetime>(), call.Arg<CommittedSessionAuthorization>().AccessRevision));
        _grants.IssueAsync(Arg.Any<GrantSubject>(), Arg.Any<CancellationToken>()).Returns(IssuedGrant);
    }

    private async Task WithProfileAsync(bool softDeleted = false)
    {
        var profile = JobSeeker.Register(
            _userId, TermsAcceptance.AcceptCurrent(FakeDateTimeProvider.Default), FakeDateTimeProvider.Default)
            .Value;
        if (softDeleted)
            profile.SoftDelete(FakeDateTimeProvider.Default);
        _db.JobSeekers.Add(profile);
        await _db.SaveChangesAsync(Ct);
    }

    private PasswordlessSessionGrant Grant()
    {
        var correlation = Substitute.For<ICorrelationIdProvider>();
        correlation.Current.Returns(Guid.NewGuid());
        var request = Substitute.For<IRequestContextProvider>();
        request.IpAddress.Returns("203.0.113.0");
        request.UserAgent.Returns("probe/1.0");
        var reader = AccountAccessTestKit.ReaderFromProfiles(_db, id => id == _userId ? _accountEmail : null,
            _ => _accessRevision, _ => _credentialCutoff, _ => _inboxConfirmed);
        var writer = AccountAccessTestKit.Advancer(reader, _coordinator, transition =>
        {
            _accessRevision = transition.AccessRevision;
            _credentialCutoff = transition.CredentialCutoff;
        });
        return new PasswordlessSessionGrant(
            _inbox, _sessions, _audit, _db, FakeDateTimeProvider.Default, correlation, request,
            reader, _coordinator, writer, new AccountAccessCleanup(_sessions, _addressChanges, _cleanupLogger));
    }

    private void FirstInboxProof()
    {
        // Retired password registration before 41a49394 produced unconfirmed accounts. NullPasswordHashes
        // later removed their hash without changing this flag; today's passwordless registration is born
        // confirmed, pinned by LoginChallengeCompleteTests.A_new_address_that_accepts_the_terms_gets_a_passwordless_account_and_a_persistent_session.
        _inboxConfirmed = false;
        _inbox.RecordAsync(_userId, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _coordinator.HasLifecycleScope.ShouldBeTrue();
            _inboxConfirmed = true;
            return InboxProof.FirstProofRecorded;
        });
    }

    // Registration is closed unless a test opens it, as it is wherever the flag is unset (ADR 0083).
    private LoginProofOutcome Outcome(bool registrationsOpen = false) => new(
        new LoginSubjectResolver(_lookup, _externalLookup, _db), Grant(), _grants, Linker(),
        Options.Create(new AuthOptions { RegistrationsOpen = registrationsOpen }), _outcomeLog);

    private ExternalLoginLinker Linker()
    {
        var correlation = Substitute.For<ICorrelationIdProvider>();
        correlation.Current.Returns(Guid.NewGuid());
        return new ExternalLoginLinker(
            _externalWriter, _db, FakeDateTimeProvider.Default, correlation, Substitute.For<IRequestContextProvider>());
    }

    private VerifyLoginChallengeCommandHandler Verify(bool registrationsOpen = false) =>
        new(_store, Outcome(registrationsOpen));

    private ConsumeLoginLinkCommandHandler Link(bool registrationsOpen = false) =>
        new(_store, Outcome(registrationsOpen));

    private void Verdict(ChallengeVerdict verdict) =>
        _store.ConsumeCodeAsync(Arg.Any<ChallengeId>(), Arg.Any<LoginCode>(), Arg.Any<CancellationToken>())
            .Returns(verdict);

    private static VerifyLoginChallengeCommand VerifyCommand() => new(ChallengeId.Generate().Reveal(), "123456");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_original_known_account_proof_cannot_become_registration_consent_after_its_address_changes(bool link)
    {
        await WithProfileAsync();
        var original = ChallengeProof();
        // A confirmed address change commits revision one while this original challenge still
        // proves the old inbox. The old address now resolves to NoAccount, not a registration right.
        _accountEmail = "new-address@example.com";
        _accessRevision = 1;
        _credentialCutoff = 1;
        _lookup.FindAccountAsync(Email, Arg.Any<CancellationToken>()).Returns((LoginAccount?)null);
        Verdict(ChallengeVerdict.Verified(original));
        var token = LoginLinkToken.FromRaw("AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8"); // gitleaks:allow
        _store.ConsumeLinkAsync(token, Arg.Any<CancellationToken>()).Returns(original);

        var result = link
            ? await Link(registrationsOpen: true).Handle(new ConsumeLoginLinkCommand(token.Reveal()), Ct)
            : await Verify(registrationsOpen: true).Handle(VerifyCommand(), Ct);

        result.Value.ShouldBeOfType<LoginOutcome.AccountUnavailable>();
        original.Access.ShouldBe(AccountAccessTestKit.Bound(_userId));
        await _grants.DidNotReceiveWithAnyArgs().IssueAsync(default!, Ct);
        await _inbox.DidNotReceiveWithAnyArgs().RecordAsync(default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default!, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateCommittedAsync(default!, default, Ct);
        await _externalWriter.DidNotReceiveWithAnyArgs().LinkAsync(default, default, default, Ct);
    }

    private LoginChallengeProof ChallengeProof(bool knownAccount = true) => new(Email)
    {
        Access = knownAccount ? AccountAccessTestKit.Bound(_userId) : new AccountAccessProof(0),
    };

    // ── the code arm's failures ──

    [Theory]
    [InlineData(2, AuthErrorCodes.LoginCodeWrong)]
    [InlineData(1, AuthErrorCodes.LoginCodeWrongLastAttempt)]
    public async Task A_wrong_code_is_a_validation_failure_that_warns_on_the_last_attempt(int remaining, string code)
    {
        Verdict(ChallengeVerdict.Wrong(remaining));

        var result = await Verify().Handle(VerifyCommand(), Ct);

        result.Error.Code.ShouldBe(code);
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
    }

    [Fact]
    public async Task A_burned_code_is_gone_as_burned()
    {
        Verdict(ChallengeVerdict.Burned);

        var result = await Verify().Handle(VerifyCommand(), Ct);

        result.Error.Code.ShouldBe(AuthErrorCodes.LoginCodeBurned);
        result.Error.Kind.ShouldBe(ErrorKind.Gone);
    }

    [Fact]
    public async Task A_missing_challenge_is_gone_as_expired()
    {
        Verdict(ChallengeVerdict.Missing);

        var result = await Verify().Handle(VerifyCommand(), Ct);

        result.Error.Code.ShouldBe(AuthErrorCodes.LoginCodeExpired);
        result.Error.Kind.ShouldBe(ErrorKind.Gone);
    }

    [Fact]
    public async Task No_failure_reaches_the_account_or_the_session_store()
    {
        foreach (var verdict in new[] { ChallengeVerdict.Wrong(2), ChallengeVerdict.Burned, ChallengeVerdict.Missing })
        {
            Verdict(verdict);
            await Verify().Handle(VerifyCommand(), Ct);
        }

        await _lookup.DidNotReceiveWithAnyArgs().FindAccountAsync(default!, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default!, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateCommittedAsync(default!, default, Ct);
    }

    [Fact]
    public async Task An_unusable_link_is_gone_and_reaches_nothing()
    {
        _store.ConsumeLinkAsync(Arg.Any<LoginLinkToken>(), Arg.Any<CancellationToken>())
            .Returns((LoginChallengeProof?)null);

        var result = await Link().Handle(new ConsumeLoginLinkCommand("token"), Ct);

        result.Error.Code.ShouldBe(AuthErrorCodes.LoginLinkUnusable);
        result.Error.Kind.ShouldBe(ErrorKind.Gone);
        await _lookup.DidNotReceiveWithAnyArgs().FindAccountAsync(default!, Ct);
    }

    // ── the outcome, resolved at proof time ──

    [Fact]
    public async Task A_verified_code_signs_an_active_account_in_and_records_the_method()
    {
        await WithProfileAsync();
        Verdict(ChallengeVerdict.Verified(ChallengeProof()));

        var result = await Verify().Handle(VerifyCommand(), Ct);

        result.Value.ShouldBe(new LoginOutcome.SignedIn("granted-session-id"));
        await _sessions.Received(1).CreateAsync(_userId, AccountAccessTestKit.Bound(_userId) with { ExpectedEmail = _accountEmail }, SessionLifetime.Persistent, Arg.Any<CancellationToken>());
        _audit.Received(1).LoginSucceeded(_userId, Arg.Any<string>(), LoginMethod.Code);
        _outcomeLog.Records.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_consumed_link_signs_an_active_account_in_and_records_the_method()
    {
        await WithProfileAsync();
        _store.ConsumeLinkAsync(Arg.Any<LoginLinkToken>(), Arg.Any<CancellationToken>())
            .Returns(ChallengeProof());

        var result = await Link().Handle(new ConsumeLoginLinkCommand("token"), Ct);

        result.Value.ShouldBe(new LoginOutcome.SignedIn("granted-session-id"));
        _audit.Received(1).LoginSucceeded(_userId, Arg.Any<string>(), LoginMethod.Link);
    }

    [Fact]
    public async Task A_soft_deleted_account_gets_its_earliest_deletion_date_and_no_session()
    {
        await WithProfileAsync(softDeleted: true);
        Verdict(ChallengeVerdict.Verified(ChallengeProof()));

        var result = await Verify().Handle(VerifyCommand(), Ct);

        // JobSeeker.SoftDelete stamps the clock's now; the job's restore window runs from there.
        result.Value.ShouldBe(new LoginOutcome.PendingDeletion(DateOnly.FromDateTime(Now.AddDays(30).UtcDateTime)));
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default!, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateCommittedAsync(default!, default, Ct);
        await _inbox.DidNotReceiveWithAnyArgs().RecordAsync(default, Ct);
    }

    [Fact]
    public async Task No_account_and_a_missing_profile_are_both_closed_registration_with_no_session()
    {
        Verdict(ChallengeVerdict.Verified(ChallengeProof()));
        (await Verify().Handle(VerifyCommand(), Ct)).Value.ShouldBeOfType<LoginOutcome.RegistrationClosed>();

        _lookup.FindAccountAsync(Email, Arg.Any<CancellationToken>()).Returns((LoginAccount?)null);
        (await Verify().Handle(VerifyCommand(), Ct)).Value.ShouldBeOfType<LoginOutcome.RegistrationClosed>();

        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default!, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateCommittedAsync(default!, default, Ct);
        await _inbox.DidNotReceiveWithAnyArgs().RecordAsync(default, Ct);

        // Nothing reaches the grant store while registration is closed.
        await _grants.DidNotReceiveWithAnyArgs().IssueAsync(default!, Ct);
    }

    // ── the open-registration arm (#1737) ──

    [Fact]
    public async Task With_registration_open_a_code_proven_new_address_gets_a_grant_for_that_address_and_no_session()
    {
        _lookup.FindAccountAsync(Email, Arg.Any<CancellationToken>()).Returns((LoginAccount?)null);
        Verdict(ChallengeVerdict.Verified(ChallengeProof(knownAccount: false)));

        var outcome = (await Verify(registrationsOpen: true).Handle(VerifyCommand(), Ct)).Value;

        outcome.ShouldBe(new LoginOutcome.ConsentRequired(IssuedGrant));
        await _grants.Received(1).IssueAsync(new GrantSubject.LoginComplete(Email), Arg.Any<CancellationToken>());
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default!, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateCommittedAsync(default!, default, Ct);
    }

    [Fact]
    public async Task With_registration_open_a_link_never_leads_to_a_grant()
    {
        // A record for an address without an account carries no link (ADR 0142 D1), so a link proven for one
        // means the account went away inside the challenge's lifetime.
        _lookup.FindAccountAsync(Email, Arg.Any<CancellationToken>()).Returns((LoginAccount?)null);
        _store.ConsumeLinkAsync(Arg.Any<LoginLinkToken>(), Arg.Any<CancellationToken>())
            .Returns(ChallengeProof());

        var outcome = (await Link(registrationsOpen: true).Handle(new ConsumeLoginLinkCommand("link-token"), Ct)).Value;

        outcome.ShouldBeOfType<LoginOutcome.AccountUnavailable>();
        await _grants.DidNotReceiveWithAnyArgs().IssueAsync(default!, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default!, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateCommittedAsync(default!, default, Ct);
    }

    [Fact]
    public async Task With_registration_open_a_missing_profile_is_unavailable_on_both_arms_and_is_never_adopted()
    {
        // The lookup finds the Identity row and no profile was seeded: ProfileMissing (#1349).
        Verdict(ChallengeVerdict.Verified(ChallengeProof()));
        _store.ConsumeLinkAsync(Arg.Any<LoginLinkToken>(), Arg.Any<CancellationToken>())
            .Returns(ChallengeProof());

        (await Verify(registrationsOpen: true).Handle(VerifyCommand(), Ct)).Value
            .ShouldBeOfType<LoginOutcome.AccountUnavailable>();
        (await Link(registrationsOpen: true).Handle(new ConsumeLoginLinkCommand("link-token"), Ct)).Value
            .ShouldBeOfType<LoginOutcome.AccountUnavailable>();

        await _grants.DidNotReceiveWithAnyArgs().IssueAsync(default!, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default!, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateCommittedAsync(default!, default, Ct);
        (await _db.JobSeekers.IgnoreQueryFilters().CountAsync(Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task With_registration_open_an_existing_account_is_answered_as_before()
    {
        await WithProfileAsync();
        Verdict(ChallengeVerdict.Verified(ChallengeProof()));

        (await Verify(registrationsOpen: true).Handle(VerifyCommand(), Ct)).Value
            .ShouldBeOfType<LoginOutcome.SignedIn>();
        await _grants.DidNotReceiveWithAnyArgs().IssueAsync(default!, Ct);
    }

    // ── the proven address must be the account's own ──

    // UNREACHABLE while registration is closed, and for a LINK in either state: the issuer then writes a
    // credential only for an active account, under the account's own spelling (LoginChallengeIssuerTests pins
    // that). Those tests assert only how the outcome refuses if a record ever proves another spelling.
    // Identity's lookup normaliser upper-cases, and U+017F (ſ) upper-cases to S, so both another letter case
    // and the long-s spelling find the account.
    private const string FoldedEmail = "perſon@example.com";

    private void TheFoldedSpellingFindsTheAccount() => TheSpellingFindsTheAccount(FoldedEmail);

    private void TheSpellingFindsTheAccount(string spelling) =>
        _lookup.FindAccountAsync(spelling, Arg.Any<CancellationToken>()).Returns(new LoginAccount(_userId, Email));

    [Theory]
    [InlineData(FoldedEmail)]
    [InlineData("Person@Example.com")]
    public async Task A_code_proving_another_spelling_of_an_accounts_address_gives_no_session(string proven)
    {
        await WithProfileAsync();
        TheSpellingFindsTheAccount(proven);
        Verdict(ChallengeVerdict.Verified(new LoginChallengeProof(proven)));

        var result = await Verify().Handle(VerifyCommand(), Ct);

        result.Value.ShouldBeOfType<LoginOutcome.RegistrationClosed>();
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default!, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateCommittedAsync(default!, default, Ct);
        await _inbox.DidNotReceiveWithAnyArgs().RecordAsync(default, Ct);
        _audit.DidNotReceiveWithAnyArgs().LoginSucceeded(default, default!, default);
        var (level, eventId, message) = _outcomeLog.Records.ShouldHaveSingleItem();
        level.ShouldBe(LogLevel.Warning);
        eventId.ShouldBe(1016);
        message.ShouldContain(_userId.ToString());
        message.ShouldContain(nameof(LoginMethod.Code));
        message.ShouldNotContain("@");
    }

    [Fact]
    public async Task A_link_proving_another_spelling_of_an_accounts_address_gives_no_session()
    {
        await WithProfileAsync();
        TheFoldedSpellingFindsTheAccount();
        _store.ConsumeLinkAsync(Arg.Any<LoginLinkToken>(), Arg.Any<CancellationToken>())
            .Returns(new LoginChallengeProof(FoldedEmail));

        var result = await Link().Handle(new ConsumeLoginLinkCommand("token"), Ct);

        result.Value.ShouldBeOfType<LoginOutcome.RegistrationClosed>();
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default!, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateCommittedAsync(default!, default, Ct);
        var (level, eventId, message) = _outcomeLog.Records.ShouldHaveSingleItem();
        level.ShouldBe(LogLevel.Warning);
        eventId.ShouldBe(1016);
        message.ShouldContain(nameof(LoginMethod.Link));
        message.ShouldNotContain("@");
    }

    // REACHABLE while registration is open: a record for an address without an account carries a code under
    // the TYPED spelling (LoginChallengeIssuer), and an account registered inside the challenge's lifetime under
    // a spelling the normaliser folds onto the same key is what the proof then resolves to.
    [Fact]
    public async Task With_registration_open_a_code_proving_another_spelling_is_unavailable_and_gets_no_grant()
    {
        await WithProfileAsync();
        TheFoldedSpellingFindsTheAccount();
        Verdict(ChallengeVerdict.Verified(new LoginChallengeProof(FoldedEmail)));

        var result = await Verify(registrationsOpen: true).Handle(VerifyCommand(), Ct);

        result.Value.ShouldBeOfType<LoginOutcome.AccountUnavailable>();
        await _grants.DidNotReceiveWithAnyArgs().IssueAsync(default!, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default!, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateCommittedAsync(default!, default, Ct);
        _outcomeLog.Records.ShouldHaveSingleItem().EventId.ShouldBe(1016);
    }

    [Fact]
    public async Task With_registration_open_a_link_proving_another_spelling_gives_no_session_and_no_grant()
    {
        await WithProfileAsync();
        TheFoldedSpellingFindsTheAccount();
        _store.ConsumeLinkAsync(Arg.Any<LoginLinkToken>(), Arg.Any<CancellationToken>())
            .Returns(new LoginChallengeProof(FoldedEmail));

        var result = await Link(registrationsOpen: true).Handle(new ConsumeLoginLinkCommand("token"), Ct);

        result.Value.ShouldBeOfType<LoginOutcome.AccountUnavailable>();
        await _grants.DidNotReceiveWithAnyArgs().IssueAsync(default!, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default!, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateCommittedAsync(default!, default, Ct);
    }

    [Fact]
    public async Task Another_spelling_of_an_address_pending_deletion_is_not_told_the_deletion_date()
    {
        await WithProfileAsync(softDeleted: true);
        TheFoldedSpellingFindsTheAccount();
        Verdict(ChallengeVerdict.Verified(new LoginChallengeProof(FoldedEmail)));

        var result = await Verify().Handle(VerifyCommand(), Ct);

        result.Value.ShouldBeOfType<LoginOutcome.RegistrationClosed>();
    }

    // ── the grant ──

    [Fact]
    public async Task A_confirmed_inbox_writes_no_audit_row_and_revokes_nothing()
    {
        await WithProfileAsync();
        await Grant().GrantAsync(new LoginSubject.Active(_userId, Email), LoginMethod.Code,
            AccountAccessTestKit.Bound(_userId), null, Ct);

        _db.AuditLogEntries.Local.ShouldBeEmpty();
        await _sessions.DidNotReceiveWithAnyArgs().InvalidateAllForUserAsync(default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().InvalidateBeforeRevisionAsync(default, default, Ct);
        await _sessions.Received(1).CreateAsync(_userId, AccountAccessTestKit.Bound(_userId) with { ExpectedEmail = _accountEmail }, SessionLifetime.Persistent, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(LoginMethod.Link)]
    [InlineData(LoginMethod.GitHub)]
    [InlineData(LoginMethod.LinkedIn)]
    public async Task A_first_inbox_proof_writes_one_audit_row_and_revokes_before_it_grants(LoginMethod method)
    {
        await WithProfileAsync();
        // GitHub's and LinkedIn's rows: the provider's flag confirms an older unconfirmed account and revokes its
        // sessions (ADR 0142 Amendments (18) and (20)).
        FirstInboxProof();

        await Grant().GrantAsync(new LoginSubject.Active(_userId, Email), method,
            AccountAccessTestKit.Bound(_userId), null, Ct);

        var row = _db.AuditLogEntries.Local.ShouldHaveSingleItem();
        row.EventType.ShouldBe(PasswordlessSessionGrant.InboxProvenAuditEventType);
        row.AggregateType.ShouldBe("User");
        row.AggregateId.ShouldBe(_userId);
        row.UserId.ShouldBe(_userId);
        Received.InOrder(() =>
        {
            _sessions.InvalidateBeforeRevisionAsync(_userId, 1, CancellationToken.None);
            _sessions.CreateCommittedAsync(Arg.Is<CommittedSessionAuthorization>(authorization =>
                authorization.UserId == _userId && authorization.AccessRevision == 1
                && authorization.Lifetime == SessionLifetime.Persistent),
                SessionLifetime.Persistent, CancellationToken.None);
            _audit.LoginSucceeded(_userId, Arg.Any<string>(), method);
        });
    }

    [Fact]
    public async Task A_committed_first_inbox_proof_keeps_its_session_receipt_when_older_session_cleanup_is_unavailable()
    {
        await WithProfileAsync();
        // SessionStoreUnavailableException is what the session store's resilience decorator throws when Redis
        // is down (#511).
        FirstInboxProof();
        _sessions.InvalidateBeforeRevisionAsync(_userId, 1, Arg.Any<CancellationToken>())
            .ThrowsAsync(new SessionStoreUnavailableException(
                "Redis-session-store är inte tillgänglig.",
                new TimeoutException("Redis timed out")));

        var receipt = await Grant().GrantAsync(new LoginSubject.Active(_userId, Email), LoginMethod.Code,
            AccountAccessTestKit.Bound(_userId), null, Ct);

        (await _db.AuditLogEntries.AsNoTracking()
            .CountAsync(e => e.EventType == PasswordlessSessionGrant.InboxProvenAuditEventType, Ct)).ShouldBe(1);
        receipt.ShouldNotBeNull().SessionId.ShouldBe("granted-session-id");
        _accessRevision.ShouldBe(1);
        _credentialCutoff.ShouldBe(1);
        _coordinator.Commits.ShouldBe(1);
        _coordinator.BegunScopes.ShouldHaveSingleItem().Lifecycle.ShouldBeTrue();
        _cleanupLogger.Records.ShouldHaveSingleItem().EventId.Id.ShouldBe(1030);
    }

    [Fact]
    public async Task An_inbox_proof_that_did_not_persist_is_followed_by_nothing()
    {
        await WithProfileAsync();
        _inbox.RecordAsync(_userId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("ConcurrencyFailure"));

        await Should.ThrowAsync<InvalidOperationException>(
            () => Grant().GrantAsync(new LoginSubject.Active(_userId, Email), LoginMethod.Code,
                AccountAccessTestKit.Bound(_userId), null, Ct));

        _db.AuditLogEntries.Local.ShouldBeEmpty();
        await _sessions.DidNotReceiveWithAnyArgs().InvalidateAllForUserAsync(default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().InvalidateBeforeRevisionAsync(default, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default!, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateCommittedAsync(default!, default, Ct);
        _audit.DidNotReceiveWithAnyArgs().LoginSucceeded(default, default!, default);
    }

    // ── #1744: a provider's proof (ADR 0142 D8, security-auditor M-1, senior-cto-advisor F2/F3) ─────────────
    // Every proof comes from the production Google adapter over a documented userinfo shape (GoogleIdentities):
    // a Workspace account on the fixture's domain, which the adapter verifies because hd is set.

    private const string Sub = "110248495921238986420";

    private static Task<ExternalLoginProof> ProviderProofAsync(string address = Email, string sub = Sub) =>
        GoogleIdentities.ProofAsync(
            GoogleUserInfoShapes.Workspace(sub, address, hostedDomain: address[(address.IndexOf('@') + 1)..]));

    private void TheLoginIsLinkedTo(Guid? userId) =>
        _externalLookup.FindUserIdAsync(ExternalProviderKey.Google, Arg.Any<ExternalSubject>(), Arg.Any<CancellationToken>())
            .Returns(userId);

    private void TheLinkWriterAnswers(ExternalLinkResult result) =>
        _externalWriter.LinkAsync(
                Arg.Any<Guid>(), ExternalProviderKey.Google, Arg.Any<ExternalSubject>(), Arg.Any<CancellationToken>())
            .Returns(result);

    [Fact]
    public async Task A_provider_proof_of_an_active_accounts_own_address_links_it_before_the_session_and_records_google()
    {
        await WithProfileAsync();
        TheLinkWriterAnswers(ExternalLinkResult.Linked);

        var outcome = await Outcome().ResolveExternalAsync(await ProviderProofAsync(), Ct);

        outcome.ShouldBeOfType<LoginOutcome.SignedIn>();
        Received.InOrder(() =>
        {
            _externalWriter.LinkAsync(
                _userId, ExternalProviderKey.Google, Arg.Is<ExternalSubject>(s => s.Reveal() == Sub), Arg.Any<CancellationToken>());
            _inbox.RecordAsync(_userId, Arg.Any<CancellationToken>());
            _sessions.CreateAsync(_userId, AccountAccessTestKit.Bound(_userId) with { ExpectedEmail = _accountEmail }, SessionLifetime.Persistent, Arg.Any<CancellationToken>());
            _audit.LoginSucceeded(_userId, Arg.Any<string>(), LoginMethod.Google);
        });
        var row = _db.AuditLogEntries.Local.ShouldHaveSingleItem();
        row.EventType.ShouldBe(ExternalLoginLinker.ExternalLoginLinkedAuditEventType);
        row.UserId.ShouldBe(_userId);
        row.Payload.ShouldBe("""{"provider":"google"}""");
    }

    [Fact]
    public async Task A_github_proof_of_an_active_accounts_own_address_links_it_before_the_session_and_records_github()
    {
        // #1745 (ADR 0142 Amendment (18)): GitHub's verified primary address takes Google's path, by Klas's decision.
        await WithProfileAsync();
        _externalWriter.LinkAsync(
                Arg.Any<Guid>(), ExternalProviderKey.GitHub, Arg.Any<ExternalSubject>(), Arg.Any<CancellationToken>())
            .Returns(ExternalLinkResult.Linked);
        var proof = await GitHubIdentities.ProofAsync(
            GitHubApiShapes.User(58323117, "person-gh"), GitHubApiShapes.Emails.PrimaryVerified(Email));

        var outcome = await Outcome().ResolveExternalAsync(proof, Ct);

        outcome.ShouldBeOfType<LoginOutcome.SignedIn>();
        Received.InOrder(() =>
        {
            _externalWriter.LinkAsync(
                _userId, ExternalProviderKey.GitHub, Arg.Is<ExternalSubject>(s => s.Reveal() == "58323117"),
                Arg.Any<CancellationToken>());
            _inbox.RecordAsync(_userId, Arg.Any<CancellationToken>());
            _sessions.CreateAsync(_userId, AccountAccessTestKit.Bound(_userId) with { ExpectedEmail = _accountEmail }, SessionLifetime.Persistent, Arg.Any<CancellationToken>());
            _audit.LoginSucceeded(_userId, Arg.Any<string>(), LoginMethod.GitHub);
        });
        _db.AuditLogEntries.Local.ShouldHaveSingleItem().Payload.ShouldBe("""{"provider":"github"}""");
    }

    [Fact]
    public async Task A_linkedin_proof_of_an_active_accounts_own_address_links_it_before_the_session_and_records_linkedin()
    {
        // #1746: LinkedIn's verified primary address takes Google's path, by Klas's decision.
        await WithProfileAsync();
        _externalWriter.LinkAsync(
                Arg.Any<Guid>(), ExternalProviderKey.LinkedIn, Arg.Any<ExternalSubject>(), Arg.Any<CancellationToken>())
            .Returns(ExternalLinkResult.Linked);
        var proof = await LinkedInIdentities.ProofAsync(
            LinkedInUserInfoShapes.Member(LinkedInUserInfoShapes.DocumentedSub, Email));

        var outcome = await Outcome().ResolveExternalAsync(proof, Ct);

        outcome.ShouldBeOfType<LoginOutcome.SignedIn>();
        Received.InOrder(() =>
        {
            _externalWriter.LinkAsync(
                _userId, ExternalProviderKey.LinkedIn, Arg.Is<ExternalSubject>(s => s.Reveal() == "782bbtaQ"),
                Arg.Any<CancellationToken>());
            _inbox.RecordAsync(_userId, Arg.Any<CancellationToken>());
            _sessions.CreateAsync(_userId, AccountAccessTestKit.Bound(_userId) with { ExpectedEmail = _accountEmail }, SessionLifetime.Persistent, Arg.Any<CancellationToken>());
            _audit.LoginSucceeded(_userId, Arg.Any<string>(), LoginMethod.LinkedIn);
        });
        _db.AuditLogEntries.Local.ShouldHaveSingleItem().Payload.ShouldBe("""{"provider":"linkedin"}""");
    }

    [Fact]
    public async Task A_provider_proof_already_linked_to_the_account_signs_in_without_writing_a_link()
    {
        await WithProfileAsync();
        TheLoginIsLinkedTo(_userId);

        (await Outcome().ResolveExternalAsync(await ProviderProofAsync(), Ct)).ShouldBeOfType<LoginOutcome.SignedIn>();

        await _externalWriter.DidNotReceiveWithAnyArgs().LinkAsync(default, default, default, Ct);
        _db.AuditLogEntries.Local.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_provider_login_another_account_holds_is_refused_and_never_moved()
    {
        // security-auditor M-1(b): the address names this account, the identifier belongs to another.
        await WithProfileAsync();
        var holder = Guid.NewGuid();
        TheLoginIsLinkedTo(holder);

        var outcome = await Outcome(registrationsOpen: true).ResolveExternalAsync(await ProviderProofAsync(), Ct);

        outcome.ShouldBeOfType<LoginOutcome.AccountUnavailable>();
        await _externalWriter.DidNotReceiveWithAnyArgs().LinkAsync(default, default, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default!, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateCommittedAsync(default!, default, Ct);
        var (_, eventId, message) = _outcomeLog.Records.ShouldHaveSingleItem();
        eventId.ShouldBe(1024);
        message.ShouldContain(holder.ToString());
        message.ShouldContain(nameof(LoginMethod.Google));
        message.ShouldNotContain(Sub);
        message.ShouldNotContain("@");
    }

    [Fact]
    public async Task A_linked_login_whose_address_changed_at_the_provider_is_refused()
    {
        // test-writer Major 8, with the expectation senior-cto-advisor F2 reversed: the identifier is linked to this
        // account, and the provider now asserts another address, which names no account. No session, no new row.
        await WithProfileAsync();
        TheLoginIsLinkedTo(_userId);

        var outcome = await Outcome(registrationsOpen: true)
            .ResolveExternalAsync(await ProviderProofAsync(address: "person.renamed@example.com"), Ct);

        outcome.ShouldBeOfType<LoginOutcome.AccountUnavailable>();
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default!, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateCommittedAsync(default!, default, Ct);
        await _grants.DidNotReceiveWithAnyArgs().IssueAsync(default!, Ct);
        var (_, eventId, message) = _outcomeLog.Records.ShouldHaveSingleItem();
        eventId.ShouldBe(1024);
        message.ShouldContain(nameof(LoginMethod.Google));
    }

    [Fact]
    public async Task A_provider_proof_whose_address_differs_from_the_accounts_only_in_ascii_case_signs_in()
    {
        // senior-cto-advisor F3 (a), security-auditor S3: the external arm matches pure-ASCII spellings case-blind.
        _lookup.FindAccountAsync(Email, Arg.Any<CancellationToken>())
            .Returns(new LoginAccount(_userId, "Person@Example.com"));
        _accountEmail = "Person@Example.com";
        await WithProfileAsync();
        TheLinkWriterAnswers(ExternalLinkResult.Linked);

        (await Outcome().ResolveExternalAsync(await ProviderProofAsync(), Ct))
            .ShouldBeOfType<LoginOutcome.SignedIn>();
    }

    [Theory]
    [InlineData(0x017F, "person@example.com", 3)]
    [InlineData(0x212A, "kalle@example.com", 0)]
    [InlineData(0x037E, "per;on@example.com", 3)]
    [InlineData(0x1FEF, "per`on@example.com", 3)]
    public async Task A_provider_proof_that_differs_by_a_folding_character_is_refused(
        int codePoint, string account, int at)
    {
        // UNREACHABLE from Google, declared: no documented shape carries it. The account's own spelling differs by
        // one character; only the refusal is asserted.
        var folded = $"{account[..at]}{(char)codePoint}{account[(at + 1)..]}";
        await TheProviderSpellingIsRefusedAsync(folded, account);
    }

    [Theory]
    [InlineData(false, typeof(LoginOutcome.RegistrationClosed))]
    [InlineData(true, typeof(LoginOutcome.AccountUnavailable))]
    public async Task A_github_proof_that_differs_by_a_folding_character_is_refused_and_links_nothing(
        bool registrationsOpen, Type expected)
    {
        // #1779 for GitHub, where it is reachable: the GitHub adapter's own rule admits the folded spelling as it
        // arrives, asserted first.
        _lookup.FindAccountAsync(FoldedEmail, Arg.Any<CancellationToken>())
            .Returns(new LoginAccount(_userId, Email));
        await WithProfileAsync();
        var proof = await GitHubIdentities.ProofAsync(
            GitHubApiShapes.User(58323117, "folded-gh"), GitHubApiShapes.Emails.PrimaryVerified(FoldedEmail));
        proof.Email.Value.ShouldBe(FoldedEmail);

        (await Outcome(registrationsOpen).ResolveExternalAsync(proof, Ct)).ShouldBeOfType(expected);

        await _externalWriter.DidNotReceiveWithAnyArgs().LinkAsync(default, default, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default!, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateCommittedAsync(default!, default, Ct);
        var (_, eventId, message) = _outcomeLog.Records.ShouldHaveSingleItem();
        eventId.ShouldBe(1016);
        message.ShouldContain(nameof(LoginMethod.GitHub));
    }

    [Theory]
    [InlineData(false, typeof(LoginOutcome.RegistrationClosed))]
    [InlineData(true, typeof(LoginOutcome.AccountUnavailable))]
    public async Task A_linkedin_proof_that_differs_by_a_folding_character_is_refused_and_links_nothing(
        bool registrationsOpen, Type expected)
    {
        // #1779 for LinkedIn: the LinkedIn adapter's own rule admits the folded spelling as it arrives, asserted first.
        _lookup.FindAccountAsync(FoldedEmail, Arg.Any<CancellationToken>())
            .Returns(new LoginAccount(_userId, Email));
        await WithProfileAsync();
        var proof = await LinkedInIdentities.ProofAsync(
            LinkedInUserInfoShapes.Member(LinkedInUserInfoShapes.DocumentedSub, FoldedEmail));
        proof.Email.Value.ShouldBe(FoldedEmail);

        (await Outcome(registrationsOpen).ResolveExternalAsync(proof, Ct)).ShouldBeOfType(expected);

        await _externalWriter.DidNotReceiveWithAnyArgs().LinkAsync(default, default, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default!, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateCommittedAsync(default!, default, Ct);
        var (_, eventId, message) = _outcomeLog.Records.ShouldHaveSingleItem();
        eventId.ShouldBe(1016);
        message.ShouldContain(nameof(LoginMethod.LinkedIn));
    }

    private async Task TheProviderSpellingIsRefusedAsync(string providerSpelling, string account)
    {
        _lookup.FindAccountAsync(providerSpelling, Arg.Any<CancellationToken>())
            .Returns(new LoginAccount(_userId, account));
        await WithProfileAsync();

        (await Outcome().ResolveExternalAsync(await ProviderProofAsync(address: providerSpelling), Ct))
            .ShouldBeOfType<LoginOutcome.RegistrationClosed>();
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default!, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateCommittedAsync(default!, default, Ct);
        var (_, eventId, message) = _outcomeLog.Records.ShouldHaveSingleItem();
        eventId.ShouldBe(1016);
        message.ShouldContain(nameof(LoginMethod.Google));
    }

    [Fact]
    public async Task With_registration_open_a_provider_proof_of_a_new_address_gets_an_external_grant_and_no_session()
    {
        const string fresh = "ny@example.com";

        var outcome = await Outcome(registrationsOpen: true).ResolveExternalAsync(await ProviderProofAsync(fresh), Ct);

        outcome.ShouldBeOfType<LoginOutcome.ConsentRequired>().Grant.ShouldBe(IssuedGrant);
        await _grants.Received(1).IssueAsync(
            Arg.Is<GrantSubject>(s => s is GrantSubject.LoginCompleteExternal
                                      && ((GrantSubject.LoginCompleteExternal)s).ProvenEmail.Value == fresh
                                      && ((GrantSubject.LoginCompleteExternal)s).Provider == ExternalProviderKey.Google
                                      && ((GrantSubject.LoginCompleteExternal)s).Subject.Reveal() == Sub),
            Arg.Any<CancellationToken>());
        await _externalWriter.DidNotReceiveWithAnyArgs().LinkAsync(default, default, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default!, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateCommittedAsync(default!, default, Ct);
    }

    [Fact]
    public async Task With_registration_closed_a_provider_proof_of_a_new_address_is_closed_with_no_grant()
    {
        (await Outcome().ResolveExternalAsync(await ProviderProofAsync("ny@example.com"), Ct))
            .ShouldBeOfType<LoginOutcome.RegistrationClosed>();

        await _grants.DidNotReceiveWithAnyArgs().IssueAsync(default!, Ct);
    }

    [Fact]
    public async Task A_provider_proof_of_an_account_pending_deletion_links_nothing_and_opens_no_session()
    {
        await WithProfileAsync(softDeleted: true);

        (await Outcome().ResolveExternalAsync(await ProviderProofAsync(), Ct))
            .ShouldBeOfType<LoginOutcome.PendingDeletion>();

        await _externalWriter.DidNotReceiveWithAnyArgs().LinkAsync(default, default, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default!, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateCommittedAsync(default!, default, Ct);
    }

    [Fact]
    public async Task With_registration_open_a_provider_proof_of_a_row_without_a_profile_links_nothing()
    {
        // AccountRegistrar at 22aefd8db committed Identity before a failed profile save. The current writer pin is
        // AccountRegistrationAtomicityTests.OpenAsync_ShouldLeaveNoIdentityOrProfile_WhenAuditSaveFails.
        (await Outcome(registrationsOpen: true).ResolveExternalAsync(await ProviderProofAsync(), Ct))
            .ShouldBeOfType<LoginOutcome.AccountUnavailable>();

        await _externalWriter.DidNotReceiveWithAnyArgs().LinkAsync(default, default, default, Ct);
    }

    [Fact]
    public async Task A_link_this_account_won_in_the_meantime_signs_in_and_writes_no_audit_row()
    {
        await WithProfileAsync();
        TheLinkWriterAnswers(ExternalLinkResult.AlreadyLinkedToThisUser);

        (await Outcome().ResolveExternalAsync(await ProviderProofAsync(), Ct)).ShouldBeOfType<LoginOutcome.SignedIn>();

        _db.AuditLogEntries.Local.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_link_another_account_won_in_the_meantime_opens_no_session()
    {
        await WithProfileAsync();
        TheLinkWriterAnswers(ExternalLinkResult.LinkedToAnotherUser);

        (await Outcome().ResolveExternalAsync(await ProviderProofAsync(), Ct))
            .ShouldBeOfType<LoginOutcome.RegistrationClosed>();

        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default!, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateCommittedAsync(default!, default, Ct);
        _db.AuditLogEntries.Local.ShouldBeEmpty();
        var (_, eventId, message) = _outcomeLog.Records.ShouldHaveSingleItem();
        eventId.ShouldBe(1025);
        message.ShouldContain(nameof(LoginMethod.Google));
    }

    [Fact]
    public async Task A_github_link_another_account_won_in_the_meantime_opens_no_session()
    {
        await WithProfileAsync();
        _externalWriter.LinkAsync(
                Arg.Any<Guid>(), ExternalProviderKey.GitHub, Arg.Any<ExternalSubject>(), Arg.Any<CancellationToken>())
            .Returns(ExternalLinkResult.LinkedToAnotherUser);
        var proof = await GitHubIdentities.ProofAsync(
            GitHubApiShapes.User(58323117, "person-gh"), GitHubApiShapes.Emails.PrimaryVerified(Email));

        (await Outcome().ResolveExternalAsync(proof, Ct)).ShouldBeOfType<LoginOutcome.RegistrationClosed>();

        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default!, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateCommittedAsync(default!, default, Ct);
        _db.AuditLogEntries.Local.ShouldBeEmpty();
        var (_, eventId, message) = _outcomeLog.Records.ShouldHaveSingleItem();
        eventId.ShouldBe(1025);
        message.ShouldContain(nameof(LoginMethod.GitHub));
    }

    [Fact]
    public async Task A_linkedin_link_another_account_won_in_the_meantime_opens_no_session()
    {
        await WithProfileAsync();
        _externalWriter.LinkAsync(
                Arg.Any<Guid>(), ExternalProviderKey.LinkedIn, Arg.Any<ExternalSubject>(), Arg.Any<CancellationToken>())
            .Returns(ExternalLinkResult.LinkedToAnotherUser);
        var proof = await LinkedInIdentities.ProofAsync(
            LinkedInUserInfoShapes.Member(LinkedInUserInfoShapes.DocumentedSub, Email));

        (await Outcome().ResolveExternalAsync(proof, Ct)).ShouldBeOfType<LoginOutcome.RegistrationClosed>();

        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default!, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateCommittedAsync(default!, default, Ct);
        _db.AuditLogEntries.Local.ShouldBeEmpty();
        var (_, eventId, message) = _outcomeLog.Records.ShouldHaveSingleItem();
        eventId.ShouldBe(1025);
        message.ShouldContain(nameof(LoginMethod.LinkedIn));
    }
}
