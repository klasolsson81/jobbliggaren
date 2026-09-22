using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Commands.VerifyEmailChangeChallenge;
using Jobbliggaren.Application.Auth.Grants;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// The change-email challenge's code arm (#1739, ADR 0142 D5). The handler passes the binding and never compares;
/// a verified code is a change-email grant for the session's user and the address the store proved; the refusals
/// are the login code's, from the one shared mapping.
/// </summary>
public sealed class VerifyEmailChangeChallengeCommandHandlerTests
{
    private const string ChallengeIdRaw = "AAECAwQFBgcICQoLDA0ODw";
    private const string CodeRaw = "042917";
    private const string ProvenEmail = "ny.adress@example.se";
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly GrantToken Grant = GrantToken.Generate();

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ILoginChallengeStore _store = Substitute.For<ILoginChallengeStore>();
    private readonly IGrantStore _grants = Substitute.For<IGrantStore>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public VerifyEmailChangeChallengeCommandHandlerTests()
    {
        _currentUser.UserId.Returns(UserId);
        _grants.IssueAsync(Arg.Any<GrantSubject>(), Arg.Any<CancellationToken>()).Returns(Grant);
    }

    private VerifyEmailChangeChallengeCommandHandler Sut() => new(_currentUser, _store, _grants);

    private static VerifyEmailChangeChallengeCommand Command => new(ChallengeIdRaw, CodeRaw);

    private void StoreAnswers(ChallengeVerdict verdict) =>
        _store.ConsumeBoundCodeAsync(
                Arg.Any<ChallengeId>(), Arg.Any<LoginCode>(), Arg.Any<ChallengeBinding>(), Arg.Any<CancellationToken>())
            .Returns(verdict);

    [Fact]
    public async Task The_code_is_presented_with_this_users_change_email_binding()
    {
        StoreAnswers(ChallengeVerdict.Verified(new LoginChallengeProof(ProvenEmail)));

        await Sut().Handle(Command, Ct);

        await _store.Received(1).ConsumeBoundCodeAsync(
            ChallengeId.FromRaw(ChallengeIdRaw),
            LoginCode.FromRaw(CodeRaw),
            new ChallengeBinding(ChallengePurpose.ChangeEmail, UserId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_verified_code_is_a_change_email_grant_for_this_user_and_the_proven_address()
    {
        StoreAnswers(ChallengeVerdict.Verified(new LoginChallengeProof(ProvenEmail)));

        var result = await Sut().Handle(Command, Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(Grant);
        await _grants.Received(1).IssueAsync(
            new GrantSubject.ChangeEmail(UserId, ProvenEmail), Arg.Any<CancellationToken>());
        await _grants.DidNotReceive().IssueAsync(
            Arg.Is<GrantSubject>(s => !(s is GrantSubject.ChangeEmail)), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(2, AuthErrorCodes.LoginCodeWrong, ErrorKind.Validation)]
    [InlineData(1, AuthErrorCodes.LoginCodeWrongLastAttempt, ErrorKind.Validation)]
    public async Task A_wrong_code_answers_the_login_codes_error_and_issues_nothing(
        int attemptsRemaining, string code, ErrorKind kind)
    {
        StoreAnswers(ChallengeVerdict.Wrong(attemptsRemaining));

        var result = await Sut().Handle(Command, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(code);
        result.Error.Kind.ShouldBe(kind);
        await _grants.DidNotReceiveWithAnyArgs().IssueAsync(default!, Ct);
    }

    [Fact]
    public async Task A_burned_code_is_gone()
    {
        StoreAnswers(ChallengeVerdict.Burned);

        var result = await Sut().Handle(Command, Ct);

        result.Error.Code.ShouldBe(AuthErrorCodes.LoginCodeBurned);
        result.Error.Kind.ShouldBe(ErrorKind.Gone);
        await _grants.DidNotReceiveWithAnyArgs().IssueAsync(default!, Ct);
    }

    [Fact]
    public async Task A_missing_challenge_is_gone_and_reads_as_expired()
    {
        // A login challenge, a re-authentication challenge and another user's all arrive here as Missing.
        StoreAnswers(ChallengeVerdict.Missing);

        var result = await Sut().Handle(Command, Ct);

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

        var result = await Sut().Handle(Command, Ct);

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
