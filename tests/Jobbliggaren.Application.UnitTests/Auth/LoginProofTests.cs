using System.Reflection;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Commands.ConsumeLoginLink;
using Jobbliggaren.Application.Auth.Commands.VerifyLoginChallenge;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Auth.LoginChallenges;
using Jobbliggaren.Infrastructure.Persistence;
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
    private readonly AppDbContext _db = TestAppDbContextFactory.Create();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public LoginProofTests()
    {
        _lookup.FindUserIdAsync(Email, Arg.Any<CancellationToken>()).Returns(_userId);
        _inbox.RecordAsync(_userId, Arg.Any<CancellationToken>()).Returns(InboxProof.AlreadyConfirmed);
        _sessions.CreateAsync(_userId, Arg.Any<SessionLifetime>(), Arg.Any<CancellationToken>())
            .Returns(call => new Session(
                SessionId.FromRaw("granted-session-id"), _userId, Now, Now.AddDays(30), call.Arg<SessionLifetime>()));
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

    private LoginProofOutcome Outcome() => new(new LoginSubjectResolver(_lookup, _db), Grant());

    private VerifyLoginChallengeCommandHandler Verify() => new(_store, Outcome());

    private ConsumeLoginLinkCommandHandler Link() => new(_store, Outcome());

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

        await _lookup.DidNotReceiveWithAnyArgs().FindUserIdAsync(default!, Ct);
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
        await _lookup.DidNotReceiveWithAnyArgs().FindUserIdAsync(default!, Ct);
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

        _lookup.FindUserIdAsync(Email, Arg.Any<CancellationToken>()).Returns((Guid?)null);
        (await Verify().Handle(VerifyCommand(), Ct)).Value.ShouldBeOfType<LoginOutcome.RegistrationClosed>();

        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default, Ct);
        await _inbox.DidNotReceiveWithAnyArgs().RecordAsync(default, Ct);
    }

    // ── the grant ──

    [Fact]
    public async Task A_confirmed_inbox_writes_no_audit_row_and_revokes_nothing()
    {
        await Grant().GrantAsync(new LoginSubject.Active(_userId), LoginMethod.Code, Ct);

        _db.AuditLogEntries.Local.ShouldBeEmpty();
        await _sessions.DidNotReceiveWithAnyArgs().InvalidateAllForUserAsync(default, Ct);
        await _sessions.Received(1).CreateAsync(_userId, SessionLifetime.Persistent, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_first_inbox_proof_writes_one_audit_row_and_revokes_before_it_grants()
    {
        _inbox.RecordAsync(_userId, Arg.Any<CancellationToken>()).Returns(InboxProof.FirstProofRecorded);

        await Grant().GrantAsync(new LoginSubject.Active(_userId), LoginMethod.Link, Ct);

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
    public async Task An_inbox_proof_that_did_not_persist_is_followed_by_nothing()
    {
        _inbox.RecordAsync(_userId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("ConcurrencyFailure"));

        await Should.ThrowAsync<InvalidOperationException>(
            () => Grant().GrantAsync(new LoginSubject.Active(_userId), LoginMethod.Code, Ct));

        _db.AuditLogEntries.Local.ShouldBeEmpty();
        await _sessions.DidNotReceiveWithAnyArgs().InvalidateAllForUserAsync(default, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default, Ct);
        _audit.DidNotReceiveWithAnyArgs().LoginSucceeded(default, default!, default);
    }

    // ── the chain's shape: one consumer per link, and no password on it ──

    private static readonly Assembly[] Production =
        [typeof(LoginProofOutcome).Assembly, typeof(IdentityInboxProofRecorder).Assembly];

    private static string[] ConsumersOf(Type dependency) =>
        [.. Production.SelectMany(a => a.GetTypes())
            .Where(t => t.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(c => c.GetParameters().Any(p => p.ParameterType == dependency)))
            .Select(t => t.FullName!)
            .Order()];

    [Fact]
    public void Only_the_grant_can_record_an_inbox_proof()
    {
        // ADR 0127 refused a bare force-confirm: confirming an address is reachable only after a proof.
        ConsumersOf(typeof(IInboxProofRecorder)).ShouldBe([typeof(PasswordlessSessionGrant).FullName!]);
    }

    [Fact]
    public void Only_the_outcome_function_can_grant_and_only_the_two_proof_handlers_can_reach_it()
    {
        ConsumersOf(typeof(PasswordlessSessionGrant)).ShouldBe([typeof(LoginProofOutcome).FullName!]);
        ConsumersOf(typeof(LoginProofOutcome)).ShouldBe(
            [typeof(ConsumeLoginLinkCommandHandler).FullName!, typeof(VerifyLoginChallengeCommandHandler).FullName!]);
    }

    [Fact]
    public void The_proof_chain_can_reach_neither_a_password_check_nor_lockout()
    {
        // Every port the two handlers can reach, following concrete classes through their constructors. The
        // account is reached through ILoginAccountLookup, which offers a lookup and nothing else.
        var reached = new HashSet<Type>();
        var pending = new Stack<Type>([typeof(VerifyLoginChallengeCommandHandler), typeof(ConsumeLoginLinkCommandHandler)]);
        while (pending.TryPop(out var type))
        {
            foreach (var parameter in type.GetConstructors().SelectMany(c => c.GetParameters()))
            {
                if (reached.Add(parameter.ParameterType) && parameter.ParameterType is { IsClass: true, IsAbstract: false })
                    pending.Push(parameter.ParameterType);
            }
        }

        reached.ShouldNotContain(typeof(IUserAccountService));
        reached.ShouldNotContain(typeof(ILoginTimingEqualizer));
        reached.ShouldContain(typeof(ILoginAccountLookup));
    }
}
