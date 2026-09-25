using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Commands.CompleteLoginChallenge;
using Jobbliggaren.Application.Auth.Grants;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Auth.Registration;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Auth.Sessions;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// #1737 — <c>complete</c>'s order and its refusals (ADR 0142 D3). The grant store and the claim are
/// substitutes answering what their Redis adapters answer (RedisGrantStoreTests and
/// RedisRegistrationClaimTests are their contracts); the account read is the real resolver over a seeded
/// in-memory context, and the session is opened by the real outcome function.
/// </summary>
public sealed class CompleteLoginChallengeTests
{
    private const string Email = "new.person@example.com";
    private static readonly GrantToken Token = GrantToken.FromRaw("AAECAwQFBgcICQoLDA0ODw");
    private static readonly DateTimeOffset Now = FakeDateTimeProvider.Default.UtcNow;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly IGrantStore _grants = Substitute.For<IGrantStore>();
    private readonly IRegistrationClaim _claim = Substitute.For<IRegistrationClaim>();
    private readonly IPasswordlessAccountCreator _accounts = Substitute.For<IPasswordlessAccountCreator>();
    private readonly ILoginAccountLookup _lookup = Substitute.For<ILoginAccountLookup>();
    private readonly IInboxProofRecorder _inbox = Substitute.For<IInboxProofRecorder>();
    private readonly ISessionStore _sessions = Substitute.For<ISessionStore>();
    private readonly AppDbContext _db = TestAppDbContextFactory.Create();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public CompleteLoginChallengeTests()
    {
        _grants.RedeemAsync(Token, Arg.Any<GrantAssertion>(), Arg.Any<CancellationToken>())
            .Returns(new GrantSubject.LoginComplete(Email));
        _claim.TryClaimAsync(Email, Arg.Any<CancellationToken>()).Returns(true);
        _inbox.RecordAsync(_userId, Arg.Any<CancellationToken>()).Returns(InboxProof.AlreadyConfirmed);
        _sessions.CreateAsync(_userId, Arg.Any<SessionLifetime>(), Arg.Any<CancellationToken>())
            .Returns(call => new Session(
                SessionId.FromRaw("granted-session-id"), _userId, Now, Now.AddDays(30), call.Arg<SessionLifetime>()));
    }

    private CompleteLoginChallengeCommandHandler Handler(bool registrationsOpen = true)
    {
        var options = Options.Create(new AuthOptions { RegistrationsOpen = registrationsOpen });
        var correlation = Substitute.For<ICorrelationIdProvider>();
        correlation.Current.Returns(Guid.NewGuid());
        var request = Substitute.For<IRequestContextProvider>();
        var resolver = new LoginSubjectResolver(_lookup, _db);
        var grant = new PasswordlessSessionGrant(
            _inbox, _sessions, Substitute.For<IAuthAuditLogger>(), _db, FakeDateTimeProvider.Default, correlation, request);

        return new CompleteLoginChallengeCommandHandler(
            options, _grants, _claim, resolver,
            new AccountRegistrar(_accounts, _db, FakeDateTimeProvider.Default, correlation, request),
            new LoginProofOutcome(resolver, grant, _grants, options, NullLogger<LoginProofOutcome>.Instance));
    }

    private static CompleteLoginChallengeCommand Command() => new(Token.Reveal(), AcceptTerms: true);

    private async Task WithAccountAsync(bool softDeleted = false)
    {
        _lookup.FindAccountAsync(Email, Arg.Any<CancellationToken>()).Returns(new LoginAccount(_userId, Email));
        var profile = JobSeeker.Register(
            _userId, TermsAcceptance.AcceptCurrent(FakeDateTimeProvider.Default), FakeDateTimeProvider.Default)
            .Value;
        if (softDeleted)
            profile.SoftDelete(FakeDateTimeProvider.Default);
        _db.JobSeekers.Add(profile);
        await _db.SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task With_registration_closed_nothing_is_redeemed_claimed_or_created()
    {
        var result = await Handler(registrationsOpen: false).Handle(Command(), Ct);

        result.Error.Code.ShouldBe(AuthErrorCodes.RegistrationsClosed);
        await _grants.DidNotReceiveWithAnyArgs().RedeemAsync(default, default!, Ct);
        await _claim.DidNotReceiveWithAnyArgs().TryClaimAsync(default!, Ct);
        await _accounts.DidNotReceiveWithAnyArgs().CreatePasswordlessUserAsync(default!, Ct);
    }

    [Fact]
    public async Task The_grant_is_redeemed_as_a_bearer_for_the_login_complete_purpose()
    {
        await WithAccountAsync();

        await Handler().Handle(Command(), Ct);

        await _grants.Received(1).RedeemAsync(
            Token,
            Arg.Is<GrantAssertion>(a => a.Purposes.Contains(GrantPurpose.LoginComplete) && a.Binding == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_grant_that_redeems_to_nothing_is_gone_and_claims_nothing()
    {
        _grants.RedeemAsync(Token, Arg.Any<GrantAssertion>(), Arg.Any<CancellationToken>())
            .Returns((GrantSubject?)null);

        var result = await Handler().Handle(Command(), Ct);

        result.Error.Code.ShouldBe(AuthErrorCodes.LoginGrantUnusable);
        result.Error.Kind.ShouldBe(ErrorKind.Gone);
        await _claim.DidNotReceiveWithAnyArgs().TryClaimAsync(default!, Ct);
        await _accounts.DidNotReceiveWithAnyArgs().CreatePasswordlessUserAsync(default!, Ct);
    }

    [Fact]
    public async Task A_lost_claim_answers_exactly_like_a_grant_that_redeems_to_nothing()
    {
        _claim.TryClaimAsync(Email, Arg.Any<CancellationToken>()).Returns(false);
        var lostClaim = await Handler().Handle(Command(), Ct);

        _grants.RedeemAsync(Token, Arg.Any<GrantAssertion>(), Arg.Any<CancellationToken>())
            .Returns((GrantSubject?)null);
        var noGrant = await Handler().Handle(Command(), Ct);

        lostClaim.Error.ShouldBe(noGrant.Error);
        await _lookup.DidNotReceiveWithAnyArgs().FindAccountAsync(default!, Ct);
        await _accounts.DidNotReceiveWithAnyArgs().CreatePasswordlessUserAsync(default!, Ct);
    }

    [Fact]
    public async Task The_claim_is_taken_after_the_grant_is_redeemed()
    {
        await WithAccountAsync();

        await Handler().Handle(Command(), Ct);

        Received.InOrder(() =>
        {
            _grants.RedeemAsync(Token, Arg.Any<GrantAssertion>(), Arg.Any<CancellationToken>());
            _claim.TryClaimAsync(Email, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task An_address_registered_meanwhile_is_signed_in_to_that_account_and_nothing_is_created()
    {
        await WithAccountAsync();

        var result = await Handler().Handle(Command(), Ct);

        result.Value.ShouldBe(new LoginOutcome.SignedIn("granted-session-id"));
        await _accounts.DidNotReceiveWithAnyArgs().CreatePasswordlessUserAsync(default!, Ct);
        (await _db.JobSeekers.IgnoreQueryFilters().CountAsync(Ct)).ShouldBe(1);
        (await _db.AuditLogEntries.CountAsync(Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task An_account_pending_deletion_meanwhile_gets_its_date_and_no_session()
    {
        await WithAccountAsync(softDeleted: true);

        var result = await Handler().Handle(Command(), Ct);

        result.Value.ShouldBeOfType<LoginOutcome.PendingDeletion>();
        await _accounts.DidNotReceiveWithAnyArgs().CreatePasswordlessUserAsync(default!, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default, Ct);
    }

    [Fact]
    public async Task An_identity_row_without_a_profile_is_unavailable_and_is_never_adopted()
    {
        _lookup.FindAccountAsync(Email, Arg.Any<CancellationToken>()).Returns(new LoginAccount(_userId, Email));

        var result = await Handler().Handle(Command(), Ct);

        result.Value.ShouldBeOfType<LoginOutcome.AccountUnavailable>();
        await _accounts.DidNotReceiveWithAnyArgs().CreatePasswordlessUserAsync(default!, Ct);
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default, Ct);
        (await _db.JobSeekers.IgnoreQueryFilters().CountAsync(Ct)).ShouldBe(0);
    }

    // ── the create arm ──

    // The address has no account when the handler asks, and the one it creates when the outcome asks.
    private void TheAddressGetsItsAccountFromTheCreator()
    {
        _lookup.FindAccountAsync(Email, Arg.Any<CancellationToken>())
            .Returns((LoginAccount?)null, new LoginAccount(_userId, Email));
        _accounts.CreatePasswordlessUserAsync(Email, Arg.Any<CancellationToken>()).Returns(Result.Success(_userId));
    }

    [Fact]
    public async Task A_new_address_gets_an_account_a_nameless_profile_with_its_terms_stamp_and_a_persistent_session()
    {
        TheAddressGetsItsAccountFromTheCreator();

        var result = await Handler().Handle(Command(), Ct);

        result.Value.ShouldBe(new LoginOutcome.SignedIn("granted-session-id"));
        await _sessions.Received(1).CreateAsync(_userId, SessionLifetime.Persistent, Arg.Any<CancellationToken>());

        var profile = await _db.JobSeekers.AsNoTracking().SingleAsync(Ct);
        profile.UserId.ShouldBe(_userId);
        profile.TermsAcceptance.ShouldBe(TermsAcceptance.AcceptCurrent(FakeDateTimeProvider.Default));
        await _accounts.DidNotReceiveWithAnyArgs().DeleteAsync(default, Ct);
    }

    [Fact]
    public async Task The_new_account_leaves_exactly_one_audit_row_and_it_names_no_address()
    {
        TheAddressGetsItsAccountFromTheCreator();

        await Handler().Handle(Command(), Ct);

        var row = await _db.AuditLogEntries.AsNoTracking().SingleAsync(Ct);
        row.EventType.ShouldBe(AccountRegistrar.AccountCreatedAuditEventType);
        row.AggregateType.ShouldBe("User");
        row.AggregateId.ShouldBe(_userId);
        row.UserId.ShouldBe(_userId);
        row.Payload.ShouldBeNull();
    }

    [Fact]
    public async Task The_profile_is_saved_before_the_session_store_is_touched()
    {
        // The outcome function reads the profile back, and a session for an account whose profile did not
        // commit is #1349. SessionStoreUnavailableException is what the store's resilience decorator throws.
        TheAddressGetsItsAccountFromTheCreator();
        _sessions.CreateAsync(_userId, Arg.Any<SessionLifetime>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new SessionStoreUnavailableException(
                "Redis-session-store är inte tillgänglig.", new TimeoutException("Redis timed out")));

        await Should.ThrowAsync<SessionStoreUnavailableException>(() => Handler().Handle(Command(), Ct).AsTask());

        (await _db.JobSeekers.AsNoTracking().CountAsync(Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task A_profile_the_aggregate_refuses_deletes_the_user_it_was_for_and_opens_no_session()
    {
        // UNREACHABLE through UserAccountService, which returns the id Identity generated: an empty id is the
        // one input JobSeeker.Register refuses here. This asserts only that the refusal is compensated.
        _lookup.FindAccountAsync(Email, Arg.Any<CancellationToken>()).Returns((LoginAccount?)null);
        _accounts.CreatePasswordlessUserAsync(Email, Arg.Any<CancellationToken>()).Returns(Result.Success(Guid.Empty));

        var result = await Handler().Handle(Command(), Ct);

        result.Error.Code.ShouldBe("JobSeeker.UserIdRequired");
        await _accounts.Received(1).DeleteAsync(Guid.Empty, Arg.Any<CancellationToken>());
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default, Ct);
        (await _db.JobSeekers.IgnoreQueryFilters().CountAsync(Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task A_duplicate_after_a_won_claim_is_signed_in_to_the_account_that_won()
    {
        // The claim does not cover every path to an account, so Identity's duplicate is what the create can
        // answer even for the caller that holds the claim. The winner's account exists by then.
        _lookup.FindAccountAsync(Email, Arg.Any<CancellationToken>()).Returns((LoginAccount?)null);
        _accounts.CreatePasswordlessUserAsync(Email, Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await WithAccountAsync();
                return Result.Failure<Guid>(DomainError.Validation(
                    AuthErrorCodes.DuplicateAccount, AuthErrorCodes.DuplicateAccountMessage));
            });

        var result = await Handler().Handle(Command(), Ct);

        result.Value.ShouldBe(new LoginOutcome.SignedIn("granted-session-id"));
        (await _db.JobSeekers.IgnoreQueryFilters().CountAsync(Ct)).ShouldBe(1);
        (await _db.AuditLogEntries.CountAsync(Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task A_create_that_fails_for_another_reason_is_that_failure_and_opens_no_session()
    {
        _lookup.FindAccountAsync(Email, Arg.Any<CancellationToken>()).Returns((LoginAccount?)null);
        _accounts.CreatePasswordlessUserAsync(Email, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<Guid>(DomainError.Validation("Auth.InvalidEmail", "Invalid email.")));

        var result = await Handler().Handle(Command(), Ct);

        result.Error.Code.ShouldBe("Auth.InvalidEmail");
        await _sessions.DidNotReceiveWithAnyArgs().CreateAsync(default, default, Ct);
        (await _db.JobSeekers.IgnoreQueryFilters().CountAsync(Ct)).ShouldBe(0);
    }
}
