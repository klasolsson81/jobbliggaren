using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.AccountEmailChanges;
using Jobbliggaren.Application.Auth.Commands.VerifyEmailChangeChallenge;
using Jobbliggaren.Application.Auth.Grants;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure.Persistence;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// The change-email challenge's code arm (#1739, ADR 0142 D5). The handler passes the binding and never compares;
/// a verified code is a change-email grant for the session's user and the address the store proved; the refusals
/// are the login code's, from the one shared mapping.
/// </summary>
public sealed class VerifyEmailChangeChallengeCommandHandlerTests : IAsyncDisposable
{
    private const string ChallengeIdRaw = "AAECAwQFBgcICQoLDA0ODw";
    private const string CodeRaw = "042917";
    private const string ProvenEmail = "ny.adress@example.se";
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly GrantToken Grant = GrantToken.Generate();

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ILoginChallengeStore _store = Substitute.For<ILoginChallengeStore>();
    private readonly IGrantStore _grants = Substitute.For<IGrantStore>();
    private readonly AppDbContext _db = TestAppDbContextFactory.Create();
    private readonly AccountAccessTestKit.RecordingAccountAccessCoordinator _coordinator = AccountAccessTestKit.Coordinator();
    private readonly FakeDateTimeProvider _clock = FakeDateTimeProvider.Default;
    private readonly IAccountEmailChangeRequests _requests;
    private readonly EmailChangeRequestProof _original = EmailChangeRequestTestKit.Original(
        ChallengeId.FromRaw(ChallengeIdRaw), FakeDateTimeProvider.Default.UtcNow);
    private bool _commitRequest = true;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public VerifyEmailChangeChallengeCommandHandlerTests()
    {
        _currentUser.UserId.Returns(UserId);
        _currentUser.AccessRevision.Returns(0L);
        _grants.IssueAsync(Arg.Any<GrantSubject>(), Arg.Any<CancellationToken>()).Returns(Grant);
        _requests = EmailChangeRequestTestKit.Reader(_db);
        _store.ReadEmailChangeRequestAsync(ChallengeId.FromRaw(ChallengeIdRaw), UserId,
            Arg.Any<CancellationToken>()).Returns(OriginalProof);
    }

    private LoginChallengeProof OriginalProof => new(ProvenEmail)
    { Access = AccountAccessTestKit.Bound(UserId), EmailChangeRequest = _original };

    private VerifyEmailChangeChallengeCommandHandler Sut() =>
        new(_currentUser, _store, _grants, AccountAccessTestKit.Reader(UserId, "person@example.com"),
            _coordinator, _requests, _clock);

    private async Task<Result<GrantToken>> HandleAsync()
    {
        if (_commitRequest)
        {
            EmailChangeRequestTestKit.AddCommittedRequest(_db, UserId, _original);
            await _db.SaveChangesAsync(Ct);
        }
        return await Sut().Handle(Command, Ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _coordinator.DisposeAsync();
        await _db.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private static VerifyEmailChangeChallengeCommand Command => new(ChallengeIdRaw, CodeRaw);

    private void StoreAnswers(ChallengeVerdict verdict) =>
        _store.ConsumeBoundCodeAsync(
                Arg.Any<ChallengeId>(), Arg.Any<LoginCode>(), Arg.Any<ChallengeBinding>(), Arg.Any<CancellationToken>())
            .Returns(verdict);

    [Fact]
    public async Task The_code_is_presented_with_this_users_change_email_binding()
    {
        StoreAnswers(ChallengeVerdict.Verified(OriginalProof));

        await HandleAsync();

        await _store.Received(1).ConsumeBoundCodeAsync(
            ChallengeId.FromRaw(ChallengeIdRaw),
            LoginCode.FromRaw(CodeRaw),
            new ChallengeBinding(ChallengePurpose.ChangeEmail, UserId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_verified_code_is_a_change_email_grant_for_this_user_and_the_proven_address()
    {
        StoreAnswers(ChallengeVerdict.Verified(OriginalProof));

        var result = await HandleAsync();

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(Grant);
        await _grants.Received(1).IssueAsync(
            new GrantSubject.ChangeEmail(UserId, ProvenEmail)
            { Access = AccountAccessTestKit.Bound(UserId), Request = _original }, Arg.Any<CancellationToken>());
        await _grants.DidNotReceive().IssueAsync(
            Arg.Is<GrantSubject>(s => !(s is GrantSubject.ChangeEmail)), Arg.Any<CancellationToken>());
        Received.InOrder(async () =>
        {
            await _store.ReadEmailChangeRequestAsync(ChallengeId.FromRaw(ChallengeIdRaw), UserId, Arg.Any<CancellationToken>());
            await _requests.HasCommittedSelfRequestAsync(UserId, _original, Arg.Any<CancellationToken>());
            await _store.ConsumeBoundCodeAsync(ChallengeId.FromRaw(ChallengeIdRaw), LoginCode.FromRaw(CodeRaw),
                new ChallengeBinding(ChallengePurpose.ChangeEmail, UserId), Arg.Any<CancellationToken>());
            await _grants.IssueAsync(Arg.Any<GrantSubject>(), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task A_staged_request_without_its_post_transport_commit_is_retryable_without_spending_a_code_attempt()
    {
        _commitRequest = false;
        StoreAnswers(ChallengeVerdict.Verified(OriginalProof));

        var result = await HandleAsync();

        result.Error.Code.ShouldBe(AuthErrorCodes.EmailChangeNotActivated);
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
        await _store.DidNotReceiveWithAnyArgs().ConsumeBoundCodeAsync(default, default, default!, Ct);
        await _grants.DidNotReceiveWithAnyArgs().IssueAsync(default!, Ct);
    }

    [Fact]
    public async Task An_audit_for_a_different_request_cannot_activate_this_request()
    {
        _commitRequest = false;
        var another = EmailChangeRequestTestKit.Original(ChallengeId.Generate(), _clock.UtcNow);
        EmailChangeRequestTestKit.AddCommittedRequest(_db, UserId, another);
        await _db.SaveChangesAsync(Ct);

        var result = await HandleAsync();

        result.Error.Code.ShouldBe(AuthErrorCodes.EmailChangeNotActivated);
        await _store.DidNotReceiveWithAnyArgs().ConsumeBoundCodeAsync(default, default, default!, Ct);
        await _grants.DidNotReceiveWithAnyArgs().IssueAsync(default!, Ct);
    }

    [Fact]
    public async Task A_genuine_legacy_self_request_is_unusable_even_before_any_credential_transition()
    {
        // The retired pre-#1976 bound writer had no activation metadata. The current writer pin is
        // ChangeEmailCommandHandlerTests.An_admitted_request_passes_the_user_budgets_then_the_address_budgets_in_that_order_then_checks_writes_and_sends.
        _store.ReadEmailChangeRequestAsync(ChallengeId.FromRaw(ChallengeIdRaw), UserId, Arg.Any<CancellationToken>())
            .Returns(new LoginChallengeProof(ProvenEmail));

        var result = await HandleAsync();

        result.IsFailure.ShouldBeTrue();
        await _store.DidNotReceiveWithAnyArgs().ConsumeBoundCodeAsync(default, default, default!, Ct);
        await _grants.DidNotReceiveWithAnyArgs().IssueAsync(default!, Ct);
    }

    [Theory]
    [InlineData(2, AuthErrorCodes.LoginCodeWrong, ErrorKind.Validation)]
    [InlineData(1, AuthErrorCodes.LoginCodeWrongLastAttempt, ErrorKind.Validation)]
    public async Task A_wrong_code_answers_the_login_codes_error_and_issues_nothing(
        int attemptsRemaining, string code, ErrorKind kind)
    {
        StoreAnswers(ChallengeVerdict.Wrong(attemptsRemaining));

        var result = await HandleAsync();

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(code);
        result.Error.Kind.ShouldBe(kind);
        await _grants.DidNotReceiveWithAnyArgs().IssueAsync(default!, Ct);
    }

    [Fact]
    public async Task A_burned_code_is_gone()
    {
        StoreAnswers(ChallengeVerdict.Burned);

        var result = await HandleAsync();

        result.Error.Code.ShouldBe(AuthErrorCodes.LoginCodeBurned);
        result.Error.Kind.ShouldBe(ErrorKind.Gone);
        await _grants.DidNotReceiveWithAnyArgs().IssueAsync(default!, Ct);
    }

    [Fact]
    public async Task A_missing_challenge_is_gone_and_reads_as_expired()
    {
        // A login challenge, a re-authentication challenge and another user's all arrive here as Missing.
        StoreAnswers(ChallengeVerdict.Missing);

        var result = await HandleAsync();

        result.Error.Code.ShouldBe(AuthErrorCodes.LoginCodeExpired);
        result.Error.Kind.ShouldBe(ErrorKind.Gone);
        await _grants.DidNotReceiveWithAnyArgs().IssueAsync(default!, Ct);
        // Never a second look in the login family: a login code proves an inbox without re-authenticating.
        await _store.DidNotReceiveWithAnyArgs().ConsumeCodeAsync(default, default, Ct);
    }

    [Fact]
    public async Task No_signed_in_user_is_refused_before_the_store_is_asked()
    {
        _currentUser.UserId.Returns((Guid?)null);

        var result = await HandleAsync();

        result.IsFailure.ShouldBeTrue();
        await _store.DidNotReceiveWithAnyArgs().ConsumeBoundCodeAsync(default, default, default!, Ct);
        await _grants.DidNotReceiveWithAnyArgs().IssueAsync(default!, Ct);
    }

    [Fact]
    public void The_code_arm_takes_neither_the_outcome_function_nor_the_session_store_nor_the_account_port()
    {
        // A bound proof must never become a session, and the verify step moves no address: that is the confirm
        // step's, behind the grant.
        var parameters = typeof(VerifyEmailChangeChallengeCommandHandler)
            .GetConstructors()
            .ShouldHaveSingleItem()
            .GetParameters()
            .Select(p => p.ParameterType)
            .ToList();

        parameters.ShouldNotContain(typeof(LoginProofOutcome));
        parameters.ShouldNotContain(typeof(LoginSubjectResolver));
        parameters.ShouldNotContain(typeof(ISessionStore));
        parameters.ShouldNotContain(typeof(PasswordlessSessionGrant));
        parameters.ShouldNotContain(typeof(IUserAccountService));
    }
}
