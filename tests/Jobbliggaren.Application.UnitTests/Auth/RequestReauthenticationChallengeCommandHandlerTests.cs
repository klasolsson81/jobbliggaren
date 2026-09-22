using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Commands.RequestReauthenticationChallenge;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// The re-authentication challenge's request path (#1739, ADR 0142 D5). Authenticated and synchronous, so
/// unlike the login arm every refusal is visible; the tests pin the ORDER of the gates, that a refused gate
/// spends nothing further, WHAT each budget is keyed by, and the record the store is asked to write.
/// </summary>
public sealed class RequestReauthenticationChallengeCommandHandlerTests
{
    private const string Email = "person@example.com";
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly LoginCode Code = LoginCode.FromRaw("042917");

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IUserAccountService _accounts = Substitute.For<IUserAccountService>();
    private readonly IEmailSender _sender = Substitute.For<IEmailSender>();
    private readonly IRateBudget _budget = Substitute.For<IRateBudget>();
    private readonly ILoginChallengeStore _store = Substitute.For<ILoginChallengeStore>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public RequestReauthenticationChallengeCommandHandlerTests()
    {
        _currentUser.UserId.Returns(UserId);
        _accounts.GetEmailAsync(UserId, Arg.Any<CancellationToken>()).Returns(Email);
        _sender.CanDeliver.Returns(true);
        _store.PutBoundAsync(Arg.Any<NewBoundChallenge>(), Arg.Any<CancellationToken>()).Returns(Code);
        Admit(cooldown: true, mails: true, codes: true);
    }

    private void Admit(bool cooldown, bool mails, bool codes)
    {
        _budget.TryConsumeAsync(
                Arg.Is<RateBudgetScope>(s => s.Name == "reauth-cooldown"), UserId.ToString(), Arg.Any<CancellationToken>())
            .Returns(cooldown);
        _budget.TryConsumeAsync(LoginChallengePolicy.MailBudget, Email, Arg.Any<CancellationToken>()).Returns(mails);
        _budget.TryConsumeAsync(LoginChallengePolicy.ReauthCodeBudget, UserId.ToString(), Arg.Any<CancellationToken>())
            .Returns(codes);
    }

    private RequestReauthenticationChallengeCommandHandler Sut(int cooldownSeconds = 60) => new(
        _currentUser,
        _accounts,
        _sender,
        _budget,
        Options.Create(new AuthEmailCooldownOptions { LoginChallengeWindowSeconds = cooldownSeconds }),
        _store);

    private static RequestReauthenticationChallengeCommand Command => new();

    [Fact]
    public async Task The_cooldown_and_the_code_budget_are_keyed_by_the_user_id_and_never_by_the_address()
    {
        // The whole hybrid rests on this (ADR 0142 D5, security-auditor): keyed by the address, anyone who knows
        // it could spend the owner's codes anonymously and block an Art. 17 deletion for a day. Only a holder
        // of the session can spend a user-keyed budget. The mail budget alone is the address's, because it
        // protects the inbox.
        await Sut().Handle(Command, Ct);

        await _budget.Received(1).TryConsumeAsync(
            Arg.Is<RateBudgetScope>(s => s.Name == "reauth-cooldown"), UserId.ToString(), Arg.Any<CancellationToken>());
        await _budget.Received(1).TryConsumeAsync(
            LoginChallengePolicy.ReauthCodeBudget, UserId.ToString(), Arg.Any<CancellationToken>());
        await _budget.Received(1).TryConsumeAsync(LoginChallengePolicy.MailBudget, Email, Arg.Any<CancellationToken>());
        await _budget.DidNotReceive().TryConsumeAsync(
            Arg.Is<RateBudgetScope>(s => s.Name != "login-challenge-mails"), Email, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_cooldown_is_one_call_per_configured_window()
    {
        await Sut(cooldownSeconds: 90).Handle(Command, Ct);

        await _budget.Received(1).TryConsumeAsync(
            Arg.Is<RateBudgetScope>(s => s.Name == "reauth-cooldown" && s.Limit == 1
                && s.Window == TimeSpan.FromSeconds(90)),
            UserId.ToString(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_sender_that_cannot_deliver_refuses_first_and_spends_no_budget()
    {
        _sender.CanDeliver.Returns(false);

        var result = await Sut().Handle(Command, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.EmailDeliveryUnavailable);
        await _budget.DidNotReceiveWithAnyArgs().TryConsumeAsync(default!, default!, Ct);
        await _store.DidNotReceiveWithAnyArgs().PutBoundAsync(default!, Ct);
        await _sender.DidNotReceiveWithAnyArgs().SendLoginChallengeAsync(default!, default!, Ct);
    }

    [Fact]
    public async Task An_admitted_request_passes_cooldown_mail_budget_and_code_budget_in_that_order_then_writes_and_sends()
    {
        var result = await Sut().Handle(Command, Ct);

        result.IsSuccess.ShouldBeTrue();
        Received.InOrder(async () =>
        {
            await _budget.TryConsumeAsync(
                Arg.Is<RateBudgetScope>(s => s.Name == "reauth-cooldown"), UserId.ToString(), Arg.Any<CancellationToken>());
            await _budget.TryConsumeAsync(LoginChallengePolicy.MailBudget, Email, Arg.Any<CancellationToken>());
            await _budget.TryConsumeAsync(LoginChallengePolicy.ReauthCodeBudget, UserId.ToString(), Arg.Any<CancellationToken>());
            await _store.PutBoundAsync(Arg.Any<NewBoundChallenge>(), Arg.Any<CancellationToken>());
            await _sender.SendLoginChallengeAsync(Email, Arg.Any<LoginChallengeEmail>(), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task The_record_is_bound_to_this_user_and_this_purpose_and_addressed_to_the_accounts_own_address()
    {
        var result = await Sut().Handle(Command, Ct);

        await _store.Received(1).PutBoundAsync(
            Arg.Is<NewBoundChallenge>(c =>
                c.Id == result.Value
                && c.Recipient == Email
                && c.Binding == new ChallengeBinding(ChallengePurpose.Reauthentication, UserId)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_mail_is_the_reauthentication_code_variant_carrying_the_minted_code()
    {
        await Sut().Handle(Command, Ct);

        await _sender.Received(1).SendLoginChallengeAsync(
            Email,
            Arg.Is<LoginChallengeEmail>(m => m is LoginChallengeEmail.ReauthenticationCode
                && ((LoginChallengeEmail.ReauthenticationCode)m).Code == Code),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_cooled_request_is_refused_visibly_and_spends_no_mail_or_code_budget()
    {
        Admit(cooldown: false, mails: true, codes: true);

        var result = await Sut().Handle(Command, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.ReauthCooldown);
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
        await _budget.DidNotReceive().TryConsumeAsync(LoginChallengePolicy.MailBudget, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _budget.DidNotReceive().TryConsumeAsync(LoginChallengePolicy.ReauthCodeBudget, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _store.DidNotReceiveWithAnyArgs().PutBoundAsync(default!, Ct);
        await _sender.DidNotReceiveWithAnyArgs().SendLoginChallengeAsync(default!, default!, Ct);
    }

    [Fact]
    public async Task A_request_over_the_shared_mail_budget_is_refused_with_the_cooldowns_error_and_spends_no_code_budget()
    {
        // The SAME code pair as the cooldown, on purpose: the mail budget is shared with the public login
        // challenge, so a refusal that told the two apart would tell a hijacked session that a login mail
        // was just requested for the address (dotnet-architect, the form round).
        Admit(cooldown: true, mails: false, codes: true);

        var result = await Sut().Handle(Command, Ct);
        Admit(cooldown: false, mails: true, codes: true);
        var cooled = await Sut().Handle(Command, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(cooled.Error);
        await _budget.DidNotReceive().TryConsumeAsync(LoginChallengePolicy.ReauthCodeBudget, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _store.DidNotReceiveWithAnyArgs().PutBoundAsync(default!, Ct);
    }

    [Fact]
    public async Task A_request_over_the_code_budget_is_refused_with_its_own_terminal_error_and_writes_nothing()
    {
        Admit(cooldown: true, mails: true, codes: false);

        var result = await Sut().Handle(Command, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.ReauthCodeBudgetExhausted);
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
        result.Error.Code.ShouldNotBe(AuthErrorCodes.ReauthCooldown);
        await _store.DidNotReceiveWithAnyArgs().PutBoundAsync(default!, Ct);
        await _sender.DidNotReceiveWithAnyArgs().SendLoginChallengeAsync(default!, default!, Ct);
    }

    [Fact]
    public async Task The_record_is_written_before_the_mail_is_sent()
    {
        await Sut().Handle(Command, Ct);

        Received.InOrder(async () =>
        {
            await _store.PutBoundAsync(Arg.Any<NewBoundChallenge>(), Arg.Any<CancellationToken>());
            await _sender.SendLoginChallengeAsync(Arg.Any<string>(), Arg.Any<LoginChallengeEmail>(), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Every_request_gets_its_own_challenge_id()
    {
        var first = await Sut().Handle(Command, Ct);
        var second = await Sut().Handle(Command, Ct);

        first.Value.ShouldNotBe(second.Value);
    }

    [Fact]
    public async Task No_signed_in_user_is_refused_before_anything_is_read()
    {
        _currentUser.UserId.Returns((Guid?)null);

        var result = await Sut().Handle(Command, Ct);

        result.IsFailure.ShouldBeTrue();
        await _accounts.DidNotReceiveWithAnyArgs().GetEmailAsync(default, Ct);
        await _budget.DidNotReceiveWithAnyArgs().TryConsumeAsync(default!, default!, Ct);
    }

    [Fact]
    public async Task A_session_whose_account_has_no_address_is_refused_before_any_budget_is_spent()
    {
        _accounts.GetEmailAsync(UserId, Arg.Any<CancellationToken>()).Returns((string?)null);

        var result = await Sut().Handle(Command, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.InvalidCredentials);
        await _budget.DidNotReceiveWithAnyArgs().TryConsumeAsync(default!, default!, Ct);
    }

    [Fact]
    public void The_request_path_takes_neither_the_outcome_function_nor_the_subject_resolver()
    {
        // The re-auth arm can never end in a session (ADR 0142 D5): it does not hold the types that mint one.
        // LoginProofChainTests pins the consumer lists; this pins the constructor, which is what the CTO bound.
        var parameters = typeof(RequestReauthenticationChallengeCommandHandler)
            .GetConstructors()
            .ShouldHaveSingleItem()
            .GetParameters()
            .Select(p => p.ParameterType)
            .ToList();

        parameters.ShouldNotContain(typeof(LoginProofOutcome));
        parameters.ShouldNotContain(typeof(LoginSubjectResolver));
        parameters.ShouldNotContain(typeof(ISessionStore));
        parameters.ShouldNotContain(typeof(ILoginChallengeDispatcher));
    }
}
