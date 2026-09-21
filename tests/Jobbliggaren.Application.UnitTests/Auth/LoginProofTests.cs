using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Commands.ConsumeLoginLink;
using Jobbliggaren.Application.Auth.Commands.VerifyLoginChallenge;
using Jobbliggaren.Application.Auth.Grants;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Auth.Sessions;
using Jobbliggaren.Infrastructure.Persistence;
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
            _userId, "Test", TermsAcceptance.AcceptCurrent(FakeDateTimeProvider.Default), FakeDateTimeProvider.Default)
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
        new LoginSubjectResolver(_lookup, _db), Grant(), _grants,
        Options.Create(new AuthOptions { RegistrationsOpen = registrationsOpen }), _outcomeLog);

    private VerifyLoginChallengeCommandHandler Verify(bool registrationsOpen = false) =>
        new(_store, Outcome(registrationsOpen));

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

    // UNREACHABLE through the current issuer: it writes a credential only for an active account, and then under
    // the account's own spelling (LoginChallengeIssuerTests pins that). These assert only how the outcome
    // refuses if a record ever proves another spelling. Identity's lookup normaliser upper-cases, and
    // U+017F (ſ) upper-cases to S, so both another letter case and the long-s spelling find the account.
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
        await Grant().GrantAsync(new LoginSubject.Active(_userId, Email), LoginMethod.Code, Ct);

        _db.AuditLogEntries.Local.ShouldBeEmpty();
        await _sessions.DidNotReceiveWithAnyArgs().InvalidateAllForUserAsync(default, Ct);
        await _sessions.Received(1).CreateAsync(_userId, SessionLifetime.Persistent, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_first_inbox_proof_writes_one_audit_row_and_revokes_before_it_grants()
    {
        _inbox.RecordAsync(_userId, Arg.Any<CancellationToken>()).Returns(InboxProof.FirstProofRecorded);

        await Grant().GrantAsync(new LoginSubject.Active(_userId, Email), LoginMethod.Link, Ct);

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
            () => Grant().GrantAsync(new LoginSubject.Active(_userId, Email), LoginMethod.Code, Ct));

        (await _db.AuditLogEntries.AsNoTracking()
            .CountAsync(e => e.EventType == PasswordlessSessionGrant.InboxProvenAuditEventType, Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task An_inbox_proof_that_did_not_persist_is_followed_by_nothing()
    {
        _inbox.RecordAsync(_userId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("ConcurrencyFailure"));

        await Should.ThrowAsync<InvalidOperationException>(
            () => Grant().GrantAsync(new LoginSubject.Active(_userId, Email), LoginMethod.Code, Ct));

        _db.AuditLogEntries.Local.ShouldBeEmpty();
        await _sessions.DidNotReceiveWithAnyArgs().InvalidateAllForUserAsync(default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default, Ct);
        _audit.DidNotReceiveWithAnyArgs().LoginSucceeded(default, default!, default);
    }
}
