using Jobbliggaren.Application.Auth;
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

    private static readonly GrantToken IssuedGrant = GrantToken.FromRaw("AAECAwQFBgcICQoLDA0ODw");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public LoginProofTests()
    {
        _lookup.FindAccountAsync(Email, Arg.Any<CancellationToken>()).Returns(new LoginAccount(_userId, Email));
        _inbox.RecordAsync(_userId, Arg.Any<CancellationToken>()).Returns(InboxProof.AlreadyConfirmed);
        _sessions.CreateAsync(_userId, Arg.Any<SessionLifetime>(), Arg.Any<CancellationToken>())
            .Returns(call => new Session(
                SessionId.FromRaw("granted-session-id"), _userId, Now, Now.AddDays(30), call.Arg<SessionLifetime>()));
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
        return new PasswordlessSessionGrant(
            _inbox, _sessions, _audit, _db, FakeDateTimeProvider.Default, correlation, request);
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
        new(_store, _grants, Outcome(registrationsOpen));

    private ConsumeLoginLinkCommandHandler Link(bool registrationsOpen = false) =>
        new(_store, Outcome(registrationsOpen));

    private void Verdict(ChallengeVerdict verdict) =>
        _store.ConsumeCodeAsync(Arg.Any<ChallengeId>(), Arg.Any<LoginCode>(), Arg.Any<CancellationToken>())
            .Returns(verdict);

    private static VerifyLoginChallengeCommand VerifyCommand() => new(ChallengeId.Generate().Reveal(), "123456");

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
        Verdict(ChallengeVerdict.Verified(new LoginChallengeProof(Email)));

        var result = await Verify().Handle(VerifyCommand(), Ct);

        result.Value.ShouldBe(new LoginOutcome.SignedIn("granted-session-id"));
        await _sessions.Received(1).CreateAsync(_userId, SessionLifetime.Persistent, Arg.Any<CancellationToken>());
        _audit.Received(1).LoginSucceeded(_userId, Arg.Any<string>(), LoginMethod.Code);
        _outcomeLog.Records.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_consumed_link_signs_an_active_account_in_and_records_the_method()
    {
        await WithProfileAsync();
        _store.ConsumeLinkAsync(Arg.Any<LoginLinkToken>(), Arg.Any<CancellationToken>())
            .Returns(new LoginChallengeProof(Email));

        var result = await Link().Handle(new ConsumeLoginLinkCommand("token"), Ct);

        result.Value.ShouldBe(new LoginOutcome.SignedIn("granted-session-id"));
        _audit.Received(1).LoginSucceeded(_userId, Arg.Any<string>(), LoginMethod.Link);
    }

    [Fact]
    public async Task A_soft_deleted_account_gets_its_earliest_deletion_date_and_no_session()
    {
        await WithProfileAsync(softDeleted: true);
        Verdict(ChallengeVerdict.Verified(new LoginChallengeProof(Email)));

        var result = await Verify().Handle(VerifyCommand(), Ct);

        // JobSeeker.SoftDelete stamps the clock's now; the job's restore window runs from there.
        result.Value.ShouldBe(new LoginOutcome.PendingDeletion(DateOnly.FromDateTime(Now.AddDays(30).UtcDateTime)));
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default, Ct);
        await _inbox.DidNotReceiveWithAnyArgs().RecordAsync(default, Ct);
    }

    [Fact]
    public async Task No_account_and_a_missing_profile_are_both_closed_registration_with_no_session()
    {
        Verdict(ChallengeVerdict.Verified(new LoginChallengeProof(Email)));
        (await Verify().Handle(VerifyCommand(), Ct)).Value.ShouldBeOfType<LoginOutcome.RegistrationClosed>();

        _lookup.FindAccountAsync(Email, Arg.Any<CancellationToken>()).Returns((LoginAccount?)null);
        (await Verify().Handle(VerifyCommand(), Ct)).Value.ShouldBeOfType<LoginOutcome.RegistrationClosed>();

        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default, Ct);
        await _inbox.DidNotReceiveWithAnyArgs().RecordAsync(default, Ct);

        // Nothing reaches the grant store while registration is closed.
        await _grants.DidNotReceiveWithAnyArgs().IssueAsync(default!, Ct);
    }

    // ── the open-registration arm (#1737) ──

    [Fact]
    public async Task With_registration_open_a_code_proven_new_address_gets_a_grant_for_that_address_and_no_session()
    {
        _lookup.FindAccountAsync(Email, Arg.Any<CancellationToken>()).Returns((LoginAccount?)null);
        Verdict(ChallengeVerdict.Verified(new LoginChallengeProof(Email)));

        var outcome = (await Verify(registrationsOpen: true).Handle(VerifyCommand(), Ct)).Value;

        outcome.ShouldBe(new LoginOutcome.ConsentRequired(IssuedGrant));
        await _grants.Received(1).IssueAsync(new GrantSubject.LoginComplete(Email), Arg.Any<CancellationToken>());
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default, Ct);
    }

    [Fact]
    public async Task With_registration_open_a_link_never_leads_to_a_grant()
    {
        // A record for an address without an account carries no link (ADR 0142 D1), so a link proven for one
        // means the account went away inside the challenge's lifetime.
        _lookup.FindAccountAsync(Email, Arg.Any<CancellationToken>()).Returns((LoginAccount?)null);
        _store.ConsumeLinkAsync(Arg.Any<LoginLinkToken>(), Arg.Any<CancellationToken>())
            .Returns(new LoginChallengeProof(Email));

        var outcome = (await Link(registrationsOpen: true).Handle(new ConsumeLoginLinkCommand("link-token"), Ct)).Value;

        outcome.ShouldBeOfType<LoginOutcome.AccountUnavailable>();
        await _grants.DidNotReceiveWithAnyArgs().IssueAsync(default!, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default, Ct);
    }

    [Fact]
    public async Task With_registration_open_a_missing_profile_is_unavailable_on_both_arms_and_is_never_adopted()
    {
        // The lookup finds the Identity row and no profile was seeded: ProfileMissing (#1349).
        Verdict(ChallengeVerdict.Verified(new LoginChallengeProof(Email)));
        _store.ConsumeLinkAsync(Arg.Any<LoginLinkToken>(), Arg.Any<CancellationToken>())
            .Returns(new LoginChallengeProof(Email));

        (await Verify(registrationsOpen: true).Handle(VerifyCommand(), Ct)).Value
            .ShouldBeOfType<LoginOutcome.AccountUnavailable>();
        (await Link(registrationsOpen: true).Handle(new ConsumeLoginLinkCommand("link-token"), Ct)).Value
            .ShouldBeOfType<LoginOutcome.AccountUnavailable>();

        await _grants.DidNotReceiveWithAnyArgs().IssueAsync(default!, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default, Ct);
        (await _db.JobSeekers.IgnoreQueryFilters().CountAsync(Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task With_registration_open_an_existing_account_is_answered_as_before()
    {
        await WithProfileAsync();
        Verdict(ChallengeVerdict.Verified(new LoginChallengeProof(Email)));

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
        await Grant().GrantAsync(new LoginSubject.Active(_userId, Email), LoginMethod.Code, SessionEvidence.InboxProven, Ct);

        _db.AuditLogEntries.Local.ShouldBeEmpty();
        await _sessions.DidNotReceiveWithAnyArgs().InvalidateAllForUserAsync(default, Ct);
        await _sessions.Received(1).CreateAsync(_userId, SessionLifetime.Persistent, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_first_inbox_proof_writes_one_audit_row_and_revokes_before_it_grants()
    {
        _inbox.RecordAsync(_userId, Arg.Any<CancellationToken>()).Returns(InboxProof.FirstProofRecorded);

        await Grant().GrantAsync(new LoginSubject.Active(_userId, Email), LoginMethod.Link, SessionEvidence.InboxProven, Ct);

        var row = _db.AuditLogEntries.Local.ShouldHaveSingleItem();
        row.EventType.ShouldBe(PasswordlessSessionGrant.InboxProvenAuditEventType);
        row.AggregateType.ShouldBe("User");
        row.AggregateId.ShouldBe(_userId);
        row.UserId.ShouldBe(_userId);
        Received.InOrder(() =>
        {
            _sessions.InvalidateAllForUserAsync(_userId, CancellationToken.None);
            _sessions.CreateAsync(_userId, SessionLifetime.Persistent, CancellationToken.None);
            _audit.LoginSucceeded(_userId, Arg.Any<string>(), LoginMethod.Link);
        });
    }

    [Fact]
    public async Task The_audit_row_is_saved_before_the_session_store_is_touched()
    {
        // SessionStoreUnavailableException is what the session store's resilience decorator throws when Redis
        // is down (#511).
        _inbox.RecordAsync(_userId, Arg.Any<CancellationToken>()).Returns(InboxProof.FirstProofRecorded);
        _sessions.InvalidateAllForUserAsync(_userId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new SessionStoreUnavailableException(
                "Redis-session-store är inte tillgänglig.",
                new TimeoutException("Redis timed out")));

        await Should.ThrowAsync<SessionStoreUnavailableException>(
            () => Grant().GrantAsync(new LoginSubject.Active(_userId, Email), LoginMethod.Code, SessionEvidence.InboxProven, Ct));

        (await _db.AuditLogEntries.AsNoTracking()
            .CountAsync(e => e.EventType == PasswordlessSessionGrant.InboxProvenAuditEventType, Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task An_inbox_proof_that_did_not_persist_is_followed_by_nothing()
    {
        _inbox.RecordAsync(_userId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("ConcurrencyFailure"));

        await Should.ThrowAsync<InvalidOperationException>(
            () => Grant().GrantAsync(new LoginSubject.Active(_userId, Email), LoginMethod.Code, SessionEvidence.InboxProven, Ct));

        _db.AuditLogEntries.Local.ShouldBeEmpty();
        await _sessions.DidNotReceiveWithAnyArgs().InvalidateAllForUserAsync(default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default, Ct);
        _audit.DidNotReceiveWithAnyArgs().LoginSucceeded(default, default!, default);
    }

    [Fact]
    public async Task A_session_on_a_bound_link_never_reaches_the_inbox_proof()
    {
        // #1745 (dotnet-architect V1, ADR 0127): a found provider link proves no inbox now. Even a recorder that would
        // answer a first proof is never asked, so nothing is confirmed and no earlier session is revoked.
        _inbox.RecordAsync(_userId, Arg.Any<CancellationToken>()).Returns(InboxProof.FirstProofRecorded);

        await Grant().GrantAsync(
            new LoginSubject.Active(_userId, Email), LoginMethod.GitHub, SessionEvidence.BoundLink, Ct);

        await _inbox.DidNotReceiveWithAnyArgs().RecordAsync(default, Ct);
        _db.AuditLogEntries.Local.ShouldBeEmpty();
        await _sessions.DidNotReceiveWithAnyArgs().InvalidateAllForUserAsync(default, Ct);
        _audit.Received(1).LoginSucceeded(_userId, Arg.Any<string>(), LoginMethod.GitHub);
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
            _sessions.CreateAsync(_userId, SessionLifetime.Persistent, Arg.Any<CancellationToken>());
            _audit.LoginSucceeded(_userId, Arg.Any<string>(), LoginMethod.Google);
        });
        var row = _db.AuditLogEntries.Local.ShouldHaveSingleItem();
        row.EventType.ShouldBe(ExternalLoginLinker.ExternalLoginLinkedAuditEventType);
        row.UserId.ShouldBe(_userId);
        row.Payload.ShouldBe("""{"provider":"google"}""");
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

    private async Task TheProviderSpellingIsRefusedAsync(string providerSpelling, string account)
    {
        _lookup.FindAccountAsync(providerSpelling, Arg.Any<CancellationToken>())
            .Returns(new LoginAccount(_userId, account));
        await WithProfileAsync();

        (await Outcome().ResolveExternalAsync(await ProviderProofAsync(address: providerSpelling), Ct))
            .ShouldBeOfType<LoginOutcome.RegistrationClosed>();
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default, Ct);
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
    }

    [Fact]
    public async Task With_registration_open_a_provider_proof_of_a_row_without_a_profile_links_nothing()
    {
        // No profile: WithProfileAsync is not called, and the lookup still finds the Identity row.
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
        _db.AuditLogEntries.Local.ShouldBeEmpty();
        var (_, eventId, message) = _outcomeLog.Records.ShouldHaveSingleItem();
        eventId.ShouldBe(1025);
        message.ShouldContain(nameof(LoginMethod.Google));
    }

    // ── #1745: a GitHub login a code binds (ADR 0142 Amendment (16); senior-cto-advisor 1a, security-auditor V-1..V-5) ──
    // Every GitHub identity comes from the production adapter over a documented /user and /user/emails shape
    // (GitHubIdentities). The pending link is the subject PendingLinkChallenge.RequestAsync issues from that proof,
    // redeemed through the grant store (its purpose-5 round trip is RedisGrantStoreTests'), so the stubbed redemption
    // answers what production writes.

    private const long GitHubId = 58323117;
    private static readonly GrantToken LinkGrant = GrantToken.FromRaw("BAECAwQFBgcICQoLDA0ODw");

    private static Task<AssertedLoginProof> GitHubProofAsync(string address = Email) =>
        GitHubIdentities.AssertedProofAsync(
            GitHubApiShapes.User(GitHubId, "person-gh"), GitHubApiShapes.Emails.PrimaryVerified(address));

    private async Task<GrantSubject.PendingExternalLink> ThePendingLinkIsForAsync(string address = Email)
    {
        var proof = await GitHubProofAsync(address);
        var pending = new GrantSubject.PendingExternalLink(proof.Address, proof.Provider, proof.Subject);
        _grants.RedeemAsync(
                LinkGrant, GrantAssertion.Bearer(GrantPurpose.PendingExternalLink), Arg.Any<CancellationToken>())
            .Returns(pending);
        return pending;
    }

    private static VerifyLoginChallengeCommand VerifyWithLinkCommand() =>
        new(ChallengeId.Generate().Reveal(), "123456", LinkGrant.Reveal());

    private void TheGitHubLoginIsLinkedTo(Guid? userId) =>
        _externalLookup.FindUserIdAsync(ExternalProviderKey.GitHub, Arg.Any<ExternalSubject>(), Arg.Any<CancellationToken>())
            .Returns(userId);

    private void TheGitHubLinkWriterAnswers(ExternalLinkResult result) =>
        _externalWriter.LinkAsync(
                Arg.Any<Guid>(), ExternalProviderKey.GitHub, Arg.Any<ExternalSubject>(), Arg.Any<CancellationToken>())
            .Returns(result);

    private static ChallengeVerdict VerdictNamed(string name) => name switch
    {
        "wrong" => ChallengeVerdict.Wrong(2),
        "wrong, last attempt" => ChallengeVerdict.Wrong(1),
        "burned" => ChallengeVerdict.Burned,
        _ => ChallengeVerdict.Missing,
    };

    [Theory]
    [InlineData("wrong")]
    [InlineData("wrong, last attempt")]
    [InlineData("burned")]
    [InlineData("missing")]
    public async Task A_pending_link_is_never_redeemed_by_a_code_that_did_not_verify(string verdict)
    {
        // Row 3 (security-auditor V-3): a typo leaves the grant for the right code. Actor: the store's verdicts.
        await ThePendingLinkIsForAsync();
        Verdict(VerdictNamed(verdict));

        (await Verify(registrationsOpen: true).Handle(VerifyWithLinkCommand(), Ct)).IsFailure.ShouldBeTrue();

        await _grants.DidNotReceiveWithAnyArgs().RedeemAsync(default, default!, Ct);
        await _externalWriter.DidNotReceiveWithAnyArgs().LinkAsync(default, default, default, Ct);
    }

    [Fact]
    public async Task A_pending_link_is_redeemed_as_a_bearer_pending_link_only_after_the_code_verified()
    {
        // Row 3: the exact assertion kills "any other purpose accepted as the pending link" (B13).
        await WithProfileAsync();
        await ThePendingLinkIsForAsync();
        TheGitHubLinkWriterAnswers(ExternalLinkResult.Linked);
        Verdict(ChallengeVerdict.Verified(new LoginChallengeProof(Email)));

        await Verify().Handle(VerifyWithLinkCommand(), Ct);

        Received.InOrder(() =>
        {
            _store.ConsumeCodeAsync(Arg.Any<ChallengeId>(), Arg.Any<LoginCode>(), Arg.Any<CancellationToken>());
            _grants.RedeemAsync(LinkGrant, GrantAssertion.Bearer(GrantPurpose.PendingExternalLink), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task A_verified_code_without_a_pending_link_redeems_nothing()
    {
        await WithProfileAsync();
        Verdict(ChallengeVerdict.Verified(new LoginChallengeProof(Email)));

        (await Verify().Handle(VerifyCommand(), Ct)).Value.ShouldBeOfType<LoginOutcome.SignedIn>();

        await _grants.DidNotReceiveWithAnyArgs().RedeemAsync(default, default!, Ct);
    }

    [Fact]
    public async Task A_code_bound_github_login_links_before_the_session_and_records_a_code_login()
    {
        // Row 5: the code proved the account's own inbox, so the login is linked, its audit row committed, and only
        // then the session opened; the session was earned by the code, so it is recorded as one.
        await WithProfileAsync();
        await ThePendingLinkIsForAsync();
        TheGitHubLinkWriterAnswers(ExternalLinkResult.Linked);
        Verdict(ChallengeVerdict.Verified(new LoginChallengeProof(Email)));

        var outcome = (await Verify().Handle(VerifyWithLinkCommand(), Ct)).Value;

        outcome.ShouldBe(new LoginOutcome.SignedIn("granted-session-id"));
        Received.InOrder(() =>
        {
            _externalWriter.LinkAsync(
                _userId, ExternalProviderKey.GitHub, Arg.Is<ExternalSubject>(s => s.Reveal() == "58323117"),
                Arg.Any<CancellationToken>());
            _sessions.CreateAsync(_userId, SessionLifetime.Persistent, Arg.Any<CancellationToken>());
            _audit.LoginSucceeded(_userId, Arg.Any<string>(), LoginMethod.Code);
        });
        var row = _db.AuditLogEntries.Local.ShouldHaveSingleItem();
        row.EventType.ShouldBe(ExternalLoginLinker.ExternalLoginLinkedAuditEventType);
        row.Payload.ShouldBe("""{"provider":"github"}""");
        _outcomeLog.Records.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_pending_link_for_one_address_and_a_code_for_another_signs_the_code_in_and_links_nothing()
    {
        // Row 4 (security-auditor V-1, V-2): both addresses are active accounts, so a binding to either has a target.
        // Actor: a client that calls verify with its own pending link and its own challenge for another address; the
        // Api takes both from the request body. Kills "bind to the grant's account after any code" (the hijack) and
        // "bind to the code's account without the address match".
        const string other = "annan@example.com";
        var otherUser = Guid.NewGuid();
        _lookup.FindAccountAsync(other, Arg.Any<CancellationToken>()).Returns(new LoginAccount(otherUser, other));
        _db.JobSeekers.Add(JobSeeker.Register(
            otherUser, TermsAcceptance.AcceptCurrent(FakeDateTimeProvider.Default), FakeDateTimeProvider.Default).Value);
        await WithProfileAsync();
        await ThePendingLinkIsForAsync(other);
        TheGitHubLinkWriterAnswers(ExternalLinkResult.Linked);
        Verdict(ChallengeVerdict.Verified(new LoginChallengeProof(Email)));

        var outcome = (await Verify().Handle(VerifyWithLinkCommand(), Ct)).Value;

        outcome.ShouldBe(new LoginOutcome.SignedIn("granted-session-id"));
        await _sessions.Received(1).CreateAsync(_userId, SessionLifetime.Persistent, Arg.Any<CancellationToken>());
        await _sessions.DidNotReceive().CreateAsync(otherUser, Arg.Any<SessionLifetime>(), Arg.Any<CancellationToken>());
        _audit.Received(1).LoginSucceeded(_userId, Arg.Any<string>(), LoginMethod.Code);
        await _externalWriter.DidNotReceiveWithAnyArgs().LinkAsync(default, default, default, Ct);
        _db.AuditLogEntries.Local.ShouldBeEmpty();
        var (_, eventId, message) = _outcomeLog.Records.ShouldHaveSingleItem();
        eventId.ShouldBe(1029);
        message.ShouldContain("Cause=AddressMismatch");
        message.ShouldContain("Provider=github");
        message.ShouldNotContain("@");
    }

    [Fact]
    public async Task A_pending_link_whose_address_differs_only_in_ascii_case_is_bound()
    {
        // security-auditor 2, signed 2026-09-26 for code-bound links only: the code went to the account's own
        // spelling, and the comparison only decides that grant and code concern the same account.
        await WithProfileAsync();
        await ThePendingLinkIsForAsync("Person@Example.com");
        TheGitHubLinkWriterAnswers(ExternalLinkResult.Linked);
        Verdict(ChallengeVerdict.Verified(new LoginChallengeProof(Email)));

        (await Verify().Handle(VerifyWithLinkCommand(), Ct)).Value.ShouldBeOfType<LoginOutcome.SignedIn>();

        await _externalWriter.Received(1).LinkAsync(
            _userId, ExternalProviderKey.GitHub, Arg.Any<ExternalSubject>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_pending_link_whose_address_differs_by_a_folding_character_is_not_bound()
    {
        // security-auditor 2(c): the ASCII bar stands, so #1779 does not reopen. DECLARED: GitHub documents no rule for
        // a non-ASCII address, though the adapter's own predicate admits this one; only the refusal to bind and the
        // code's own outcome are asserted.
        await WithProfileAsync();
        await ThePendingLinkIsForAsync(FoldedEmail);
        Verdict(ChallengeVerdict.Verified(new LoginChallengeProof(Email)));

        (await Verify().Handle(VerifyWithLinkCommand(), Ct)).Value.ShouldBeOfType<LoginOutcome.SignedIn>();

        await _externalWriter.DidNotReceiveWithAnyArgs().LinkAsync(default, default, default, Ct);
        _outcomeLog.Records.ShouldHaveSingleItem().Message.ShouldContain("Cause=AddressMismatch");
    }

    [Theory]
    [InlineData(FoldedEmail, false)]
    [InlineData(FoldedEmail, true)]
    [InlineData("Person@Example.com", false)]
    [InlineData("Person@Example.com", true)]
    public async Task A_code_bound_link_proving_another_spelling_of_an_accounts_address_binds_nothing_and_opens_no_session(
        string proven, bool registrationsOpen)
    {
        // #1779 on the new path (security-auditor signature condition (c)). Actors: the issuer's no-account branch
        // records the spelling it was given (LoginChallengeIssuer), someone registers the fold-equivalent address
        // inside the challenge's 15 minutes, and the adapter admits the spelling (its own predicate, as the folding row
        // above declares). The grant and the code agree, so only the binding's ordinal check can refuse it.
        await WithProfileAsync();
        TheSpellingFindsTheAccount(proven);
        await ThePendingLinkIsForAsync(proven);
        Verdict(ChallengeVerdict.Verified(new LoginChallengeProof(proven)));

        var outcome = (await Verify(registrationsOpen).Handle(VerifyWithLinkCommand(), Ct)).Value;

        outcome.ShouldBeOfType(registrationsOpen ? typeof(LoginOutcome.AccountUnavailable) : typeof(LoginOutcome.RegistrationClosed));
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default, Ct);
        await _externalWriter.DidNotReceiveWithAnyArgs().LinkAsync(default, default, default, Ct);
        await _inbox.DidNotReceiveWithAnyArgs().RecordAsync(default, Ct);
        var (_, eventId, message) = _outcomeLog.Records.ShouldHaveSingleItem();
        eventId.ShouldBe(1016);
        message.ShouldContain(nameof(LoginMethod.Code));
    }

    [Fact]
    public async Task A_code_bound_github_login_of_a_new_address_waits_for_the_terms_with_the_link_in_its_grant()
    {
        // Row 6: never purpose 1, which would drop the link at complete (B11). The grant carries the address the
        // CODE proved, never the asserted one (dotnet-architect R2).
        _lookup.FindAccountAsync(Email, Arg.Any<CancellationToken>()).Returns((LoginAccount?)null);
        var pending = await ThePendingLinkIsForAsync();
        Verdict(ChallengeVerdict.Verified(new LoginChallengeProof(Email)));

        var outcome = (await Verify(registrationsOpen: true).Handle(VerifyWithLinkCommand(), Ct)).Value;

        outcome.ShouldBe(new LoginOutcome.ConsentRequired(IssuedGrant));
        await _grants.Received(1).IssueAsync(
            new GrantSubject.LoginCompleteWithLink(Email, ExternalProviderKey.GitHub, pending.Subject),
            Arg.Any<CancellationToken>());
        await _grants.DidNotReceive().IssueAsync(Arg.Any<GrantSubject.LoginComplete>(), Arg.Any<CancellationToken>());
        await _externalWriter.DidNotReceiveWithAnyArgs().LinkAsync(default, default, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default, Ct);
    }

    [Fact]
    public async Task A_code_bound_github_login_of_a_new_address_is_closed_with_no_grant_while_registration_is_closed()
    {
        // Row 7: the operator's kill-switch is honoured at proof time (LoginProofOutcome resolves then).
        _lookup.FindAccountAsync(Email, Arg.Any<CancellationToken>()).Returns((LoginAccount?)null);
        await ThePendingLinkIsForAsync();
        Verdict(ChallengeVerdict.Verified(new LoginChallengeProof(Email)));

        (await Verify(registrationsOpen: false).Handle(VerifyWithLinkCommand(), Ct)).Value
            .ShouldBeOfType<LoginOutcome.RegistrationClosed>();

        await _grants.DidNotReceiveWithAnyArgs().IssueAsync(default!, Ct);
        await _externalWriter.DidNotReceiveWithAnyArgs().LinkAsync(default, default, default, Ct);
    }

    [Fact]
    public async Task A_code_bound_github_login_of_an_account_pending_deletion_links_nothing()
    {
        // Row 13: the holder's own deletion request inside the challenge's 15 minutes. Kills "link before the switch".
        await WithProfileAsync(softDeleted: true);
        await ThePendingLinkIsForAsync();
        Verdict(ChallengeVerdict.Verified(new LoginChallengeProof(Email)));

        (await Verify().Handle(VerifyWithLinkCommand(), Ct)).Value.ShouldBeOfType<LoginOutcome.PendingDeletion>();

        await _externalWriter.DidNotReceiveWithAnyArgs().LinkAsync(default, default, default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default, Ct);
    }

    [Fact]
    public async Task A_code_bound_github_login_of_a_row_without_a_profile_links_nothing()
    {
        // security-auditor V-2: no link on an account that cannot be given a session. No profile is seeded.
        await ThePendingLinkIsForAsync();
        Verdict(ChallengeVerdict.Verified(new LoginChallengeProof(Email)));

        (await Verify(registrationsOpen: true).Handle(VerifyWithLinkCommand(), Ct)).Value
            .ShouldBeOfType<LoginOutcome.AccountUnavailable>();

        await _externalWriter.DidNotReceiveWithAnyArgs().LinkAsync(default, default, default, Ct);
    }

    [Fact]
    public async Task An_expired_pending_link_leaves_the_codes_own_outcome()
    {
        // Row 14: Redis TTL ends the grant after 10 minutes, the code lives 15. The store then redeems nothing.
        await WithProfileAsync();
        _grants.RedeemAsync(LinkGrant, Arg.Any<GrantAssertion>(), Arg.Any<CancellationToken>())
            .Returns((GrantSubject?)null);
        Verdict(ChallengeVerdict.Verified(new LoginChallengeProof(Email)));

        (await Verify().Handle(VerifyWithLinkCommand(), Ct)).Value.ShouldBe(new LoginOutcome.SignedIn("granted-session-id"));

        await _externalWriter.DidNotReceiveWithAnyArgs().LinkAsync(default, default, default, Ct);
        _audit.Received(1).LoginSucceeded(_userId, Arg.Any<string>(), LoginMethod.Code);
        _outcomeLog.Records.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_pending_link_another_account_holds_meanwhile_costs_the_link_and_never_the_session()
    {
        // security-auditor V-2: the pending link can only add a link to what the code earns. Actor: the same GitHub
        // user completing a first login for another account in a second browser inside the grant's 10 minutes.
        var holder = Guid.NewGuid();
        await WithProfileAsync();
        await ThePendingLinkIsForAsync();
        TheGitHubLoginIsLinkedTo(holder);
        Verdict(ChallengeVerdict.Verified(new LoginChallengeProof(Email)));

        (await Verify().Handle(VerifyWithLinkCommand(), Ct)).Value.ShouldBe(new LoginOutcome.SignedIn("granted-session-id"));

        await _externalWriter.DidNotReceiveWithAnyArgs().LinkAsync(default, default, default, Ct);
        var (_, eventId, message) = _outcomeLog.Records.ShouldHaveSingleItem();
        eventId.ShouldBe(1029);
        message.ShouldContain("Cause=LinkedElsewhere");
        message.ShouldNotContain(holder.ToString());
    }

    [Fact]
    public async Task A_pending_link_another_account_holds_leaves_a_new_address_the_codes_own_grant()
    {
        // security-auditor V-2: a new address then earns purpose 1, never 6, and the held login is never moved.
        _lookup.FindAccountAsync(Email, Arg.Any<CancellationToken>()).Returns((LoginAccount?)null);
        await ThePendingLinkIsForAsync();
        TheGitHubLoginIsLinkedTo(Guid.NewGuid());
        Verdict(ChallengeVerdict.Verified(new LoginChallengeProof(Email)));

        (await Verify(registrationsOpen: true).Handle(VerifyWithLinkCommand(), Ct)).Value
            .ShouldBe(new LoginOutcome.ConsentRequired(IssuedGrant));

        await _grants.Received(1).IssueAsync(new GrantSubject.LoginComplete(Email), Arg.Any<CancellationToken>());
        await _grants.DidNotReceive().IssueAsync(
            Arg.Any<GrantSubject.LoginCompleteWithLink>(), Arg.Any<CancellationToken>());
        _outcomeLog.Records.ShouldHaveSingleItem().Message.ShouldContain("Cause=LinkedElsewhere");
    }

    [Fact]
    public async Task A_link_another_account_wins_at_the_write_costs_the_link_and_never_the_session()
    {
        // Minor E, decided by security-auditor V-2: the write loses to another account between the read and the
        // insert (IdentityExternalLoginStore reports LinkedToAnotherUser). The code earned the session.
        await WithProfileAsync();
        await ThePendingLinkIsForAsync();
        TheGitHubLinkWriterAnswers(ExternalLinkResult.LinkedToAnotherUser);
        Verdict(ChallengeVerdict.Verified(new LoginChallengeProof(Email)));

        (await Verify().Handle(VerifyWithLinkCommand(), Ct)).Value.ShouldBe(new LoginOutcome.SignedIn("granted-session-id"));

        _db.AuditLogEntries.Local.ShouldBeEmpty();
        var (_, eventId, message) = _outcomeLog.Records.ShouldHaveSingleItem();
        eventId.ShouldBe(1029);
        message.ShouldContain("Cause=LinkLost");
    }

    [Fact]
    public async Task A_found_github_link_signs_in_as_github_and_never_writes_a_link_or_an_inbox_proof()
    {
        // Row 8, the second GitHub login: security-auditor V-5, dotnet-architect V1. The link is one the code-bound
        // path wrote (A_code_bound_github_login_links_before_the_session_and_records_a_code_login).
        await WithProfileAsync();
        TheGitHubLoginIsLinkedTo(_userId);

        var outcome = await Outcome(registrationsOpen: true).ResolveFoundLinkAsync(await GitHubProofAsync(), Ct);

        outcome.ShouldBe(new LoginOutcome.SignedIn("granted-session-id"));
        _audit.Received(1).LoginSucceeded(_userId, Arg.Any<string>(), LoginMethod.GitHub);
        await _externalWriter.DidNotReceiveWithAnyArgs().LinkAsync(default, default, default, Ct);
        await _inbox.DidNotReceiveWithAnyArgs().RecordAsync(default, Ct);
        await _grants.DidNotReceiveWithAnyArgs().IssueAsync(default!, Ct);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_found_github_link_whose_address_folds_onto_the_accounts_is_refused(bool registrationsOpen)
    {
        // security-auditor V-5 / M-1(a): the found path still checks that GitHub names the account's own address.
        // Actors: the code-bound path wrote the link, the GitHub user then set a fold-equivalent primary, and the
        // adapter admits it (DECLARED, as the folding row above). The identifier is linked to this very account, so
        // only the address check refuses it.
        await WithProfileAsync();
        TheGitHubLoginIsLinkedTo(_userId);
        TheFoldedSpellingFindsTheAccount();

        var outcome = await Outcome(registrationsOpen).ResolveFoundLinkAsync(await GitHubProofAsync(FoldedEmail), Ct);

        outcome.ShouldBeOfType(registrationsOpen ? typeof(LoginOutcome.AccountUnavailable) : typeof(LoginOutcome.RegistrationClosed));
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default, Ct);
        await _externalWriter.DidNotReceiveWithAnyArgs().LinkAsync(default, default, default, Ct);
        await _grants.DidNotReceiveWithAnyArgs().IssueAsync(default!, Ct);
        var (_, eventId, message) = _outcomeLog.Records.ShouldHaveSingleItem();
        eventId.ShouldBe(1016);
        message.ShouldContain(nameof(LoginMethod.GitHub));
    }

    [Fact]
    public async Task A_found_github_link_whose_address_differs_only_in_ascii_case_signs_in()
    {
        // The control for the row above: the case-blind ASCII branch of ExternalAddressMatch admits it (an ordinal
        // comparison here would refuse it).
        await WithProfileAsync();
        TheGitHubLoginIsLinkedTo(_userId);
        TheSpellingFindsTheAccount("Person@Example.com");

        var outcome = await Outcome().ResolveFoundLinkAsync(await GitHubProofAsync("Person@Example.com"), Ct);

        outcome.ShouldBe(new LoginOutcome.SignedIn("granted-session-id"));
        _audit.Received(1).LoginSucceeded(_userId, Arg.Any<string>(), LoginMethod.GitHub);
    }

    [Fact]
    public async Task A_github_login_without_a_link_is_no_outcome_and_reads_no_account()
    {
        // The found path answers null for "no link", and then no account has been read (dotnet-architect R4).
        await WithProfileAsync();

        (await Outcome(registrationsOpen: true).ResolveFoundLinkAsync(await GitHubProofAsync(), Ct)).ShouldBeNull();

        await _lookup.DidNotReceiveWithAnyArgs().FindAccountAsync(default!, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default, Ct);
    }
}
