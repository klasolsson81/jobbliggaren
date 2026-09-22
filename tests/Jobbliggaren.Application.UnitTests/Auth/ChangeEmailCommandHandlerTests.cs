using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Commands.ChangeEmail;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// The change-email request step (#679; two codes since #1739, ADR 0142 D5). The caller has re-authenticated, so
/// every refusal is visible. The tests pin the ORDER of the gates, that a refused gate spends nothing after it,
/// WHAT each budget is keyed by, the record the store is asked to write and the mail the new address gets. The
/// request step never changes the address and never touches a session.
/// </summary>
public sealed class ChangeEmailCommandHandlerTests
{
    private const string Grant = "an-opaque-reauth-grant"; // gitleaks:allow
    private const string NewEmail = "ny.adress@example.se";
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly LoginCode Code = LoginCode.FromRaw("042917");

    // Not the 60 s default, so a handler reading another option's window would be seen.
    private const int ChangeEmailWindowSeconds = 137;
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(ChangeEmailWindowSeconds);

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IUserAccountService _accounts = Substitute.For<IUserAccountService>();
    private readonly IEmailSender _sender = Substitute.For<IEmailSender>();
    private readonly IRateBudget _budget = Substitute.For<IRateBudget>();
    private readonly ILoginChallengeStore _store = Substitute.For<ILoginChallengeStore>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ChangeEmailCommandHandlerTests()
    {
        _currentUser.UserId.Returns(UserId);
        _sender.CanDeliver.Returns(true);
        _accounts.CheckAddressIsFreeAsync(UserId, NewEmail, Arg.Any<CancellationToken>()).Returns(Result.Success());
        _store.PutBoundAsync(Arg.Any<NewBoundChallenge>(), Arg.Any<CancellationToken>()).Returns(Code);
        Admit(user: true, daily: true, target: true);
    }

    private void Admit(bool user, bool daily, bool target, bool targetDaily = true)
    {
        _budget.TryConsumeAsync(ChangeEmailPolicy.UserCooldown(Window), UserId.ToString(), Arg.Any<CancellationToken>())
            .Returns(user);
        _budget.TryConsumeAsync(ChangeEmailPolicy.DailyTargetBudget, UserId.ToString(), Arg.Any<CancellationToken>())
            .Returns(daily);
        _budget.TryConsumeAsync(ChangeEmailPolicy.TargetCooldown(Window), NewEmail, Arg.Any<CancellationToken>())
            .Returns(target);
        _budget.TryConsumeAsync(ChangeEmailPolicy.PerTargetDailyBudget, NewEmail, Arg.Any<CancellationToken>())
            .Returns(targetDaily);
    }

    private ChangeEmailCommandHandler Sut() => new(
        _currentUser,
        _accounts,
        _sender,
        _budget,
        Options.Create(new AuthEmailCooldownOptions { ChangeEmailWindowSeconds = ChangeEmailWindowSeconds }),
        _store);

    private static ChangeEmailCommand Command => new(Grant, NewEmail);

    [Fact]
    public async Task An_admitted_request_passes_the_user_budgets_then_the_address_budgets_in_that_order_then_checks_writes_and_sends()
    {
        var result = await Sut().Handle(Command, Ct);

        result.IsSuccess.ShouldBeTrue();
        Received.InOrder(async () =>
        {
            await _budget.TryConsumeAsync(ChangeEmailPolicy.UserCooldown(Window), UserId.ToString(), Arg.Any<CancellationToken>());
            await _budget.TryConsumeAsync(ChangeEmailPolicy.DailyTargetBudget, UserId.ToString(), Arg.Any<CancellationToken>());
            await _budget.TryConsumeAsync(ChangeEmailPolicy.TargetCooldown(Window), NewEmail, Arg.Any<CancellationToken>());
            await _budget.TryConsumeAsync(ChangeEmailPolicy.PerTargetDailyBudget, NewEmail, Arg.Any<CancellationToken>());
            await _accounts.CheckAddressIsFreeAsync(UserId, NewEmail, Arg.Any<CancellationToken>());
            await _store.PutBoundAsync(Arg.Any<NewBoundChallenge>(), Arg.Any<CancellationToken>());
            await _sender.SendLoginChallengeAsync(NewEmail, Arg.Any<LoginChallengeEmail>(), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task The_request_spends_exactly_its_four_budgets_and_never_the_shared_login_mail_budget()
    {
        // The public login arm spends login-challenge-mails anonymously, before any lookup, so consulting it here
        // would let a stranger who knows the target address hold the change off (security-auditor, PR 4's
        // pre-code round).
        await Sut().Handle(Command, Ct);

        await _budget.ReceivedWithAnyArgs(4).TryConsumeAsync(default!, default!, Ct);
        await _budget.DidNotReceive().TryConsumeAsync(
            LoginChallengePolicy.MailBudget, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _budget.DidNotReceive().TryConsumeAsync(
            LoginChallengePolicy.CodeBudget, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Both_cooldowns_are_one_call_per_configured_change_email_window()
    {
        ChangeEmailPolicy.UserCooldown(Window).Limit.ShouldBe(1);
        ChangeEmailPolicy.TargetCooldown(Window).Limit.ShouldBe(1);

        await Sut().Handle(Command, Ct);

        await _budget.Received(1).TryConsumeAsync(
            Arg.Is<RateBudgetScope>(s => s.Name == "change-email-user" && s.Limit == 1 && s.Window == Window),
            UserId.ToString(), Arg.Any<CancellationToken>());
        await _budget.Received(1).TryConsumeAsync(
            Arg.Is<RateBudgetScope>(s => s.Name == "change-email-target" && s.Limit == 1 && s.Window == Window),
            NewEmail, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_record_is_bound_to_this_user_and_the_change_email_purpose_and_addressed_to_the_new_address()
    {
        var result = await Sut().Handle(Command, Ct);

        result.Value.UserId.ShouldBe(UserId);
        await _store.Received(1).PutBoundAsync(
            Arg.Is<NewBoundChallenge>(c =>
                c.Id == result.Value.ChallengeId
                && c.Recipient == NewEmail
                && c.Binding == new ChallengeBinding(ChallengePurpose.ChangeEmail, UserId)),
            Arg.Any<CancellationToken>());

        // The address is the validated command's; the account's own is never read.
        await _accounts.DidNotReceiveWithAnyArgs().GetEmailAsync(default, Ct);
    }

    [Fact]
    public async Task The_mail_is_the_address_change_variant_carrying_the_minted_code()
    {
        await Sut().Handle(Command, Ct);

        await _sender.Received(1).SendLoginChallengeAsync(
            NewEmail,
            Arg.Is<LoginChallengeEmail>(m => m == new LoginChallengeEmail.AddressChangeCode(Code)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_cooled_user_is_refused_visibly_and_spends_neither_the_daily_cap_nor_the_shared_target_cooldown()
    {
        Admit(user: false, daily: true, target: true);

        var result = await Sut().Handle(Command, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.ChangeEmailCooldown);
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
        await _budget.DidNotReceive().TryConsumeAsync(ChangeEmailPolicy.DailyTargetBudget, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _budget.DidNotReceive().TryConsumeAsync(Arg.Any<RateBudgetScope>(), NewEmail, Arg.Any<CancellationToken>());
        await _accounts.DidNotReceiveWithAnyArgs().CheckAddressIsFreeAsync(default, default!, Ct);
        await _store.DidNotReceiveWithAnyArgs().PutBoundAsync(default!, Ct);
        await _sender.DidNotReceiveWithAnyArgs().SendLoginChallengeAsync(default!, default!, Ct);
    }

    [Fact]
    public async Task A_user_over_the_daily_cap_is_refused_with_its_own_error_and_spends_no_target_cooldown()
    {
        Admit(user: true, daily: false, target: true);

        var result = await Sut().Handle(Command, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.ChangeEmailTargetBudgetExhausted);
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
        await _budget.DidNotReceive().TryConsumeAsync(Arg.Any<RateBudgetScope>(), NewEmail, Arg.Any<CancellationToken>());
        await _accounts.DidNotReceiveWithAnyArgs().CheckAddressIsFreeAsync(default, default!, Ct);
        await _store.DidNotReceiveWithAnyArgs().PutBoundAsync(default!, Ct);
        await _sender.DidNotReceiveWithAnyArgs().SendLoginChallengeAsync(default!, default!, Ct);
    }

    [Fact]
    public async Task A_cooled_target_is_refused_with_the_user_cooldowns_error_so_the_caller_cannot_tell_them_apart()
    {
        // The target cooldown is shared between users; a code of its own would tell the caller that someone
        // asked for the same address a moment ago.
        Admit(user: true, daily: true, target: false);
        var targetCooled = await Sut().Handle(Command, Ct);

        Admit(user: false, daily: true, target: true);
        var userCooled = await Sut().Handle(Command, Ct);

        targetCooled.IsFailure.ShouldBeTrue();
        targetCooled.Error.ShouldBe(userCooled.Error);
        await _accounts.DidNotReceiveWithAnyArgs().CheckAddressIsFreeAsync(default, default!, Ct);
        await _store.DidNotReceiveWithAnyArgs().PutBoundAsync(default!, Ct);
        await _sender.DidNotReceiveWithAnyArgs().SendLoginChallengeAsync(default!, default!, Ct);
    }

    [Fact]
    public async Task A_target_over_its_daily_cap_is_refused_with_the_user_cooldowns_error_and_writes_and_sends_nothing()
    {
        // The per-address cap bounds guessing per address, whoever asks; like the target cooldown it is shared
        // between users, so it answers the cooldown's code.
        Admit(user: true, daily: true, target: true, targetDaily: false);
        var capped = await Sut().Handle(Command, Ct);

        Admit(user: false, daily: true, target: true);
        var userCooled = await Sut().Handle(Command, Ct);

        capped.IsFailure.ShouldBeTrue();
        capped.Error.ShouldBe(userCooled.Error);
        await _accounts.DidNotReceiveWithAnyArgs().CheckAddressIsFreeAsync(default, default!, Ct);
        await _store.DidNotReceiveWithAnyArgs().PutBoundAsync(default!, Ct);
        await _sender.DidNotReceiveWithAnyArgs().SendLoginChallengeAsync(default!, default!, Ct);
    }

    [Fact]
    public async Task A_taken_or_unstorable_address_is_the_ports_refusal_and_writes_and_sends_nothing()
    {
        var taken = DomainError.Conflict(AuthErrorCodes.EmailTaken, AuthErrorCodes.EmailTakenMessage);
        _accounts.CheckAddressIsFreeAsync(UserId, NewEmail, Arg.Any<CancellationToken>()).Returns(Result.Failure(taken));

        var result = await Sut().Handle(Command, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(taken);
        await _store.DidNotReceiveWithAnyArgs().PutBoundAsync(default!, Ct);
        await _sender.DidNotReceiveWithAnyArgs().SendLoginChallengeAsync(default!, default!, Ct);
    }

    [Fact]
    public async Task A_sender_that_cannot_deliver_refuses_before_any_budget_is_spent()
    {
        _sender.CanDeliver.Returns(false);

        var result = await Sut().Handle(Command, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.EmailDeliveryUnavailable);
        await _budget.DidNotReceiveWithAnyArgs().TryConsumeAsync(default!, default!, Ct);
        await _accounts.DidNotReceiveWithAnyArgs().CheckAddressIsFreeAsync(default, default!, Ct);
        await _store.DidNotReceiveWithAnyArgs().PutBoundAsync(default!, Ct);
        await _sender.DidNotReceiveWithAnyArgs().SendLoginChallengeAsync(default!, default!, Ct);
    }

    [Fact]
    public async Task A_send_that_throws_propagates()
    {
        _sender.SendLoginChallengeAsync(NewEmail, Arg.Any<LoginChallengeEmail>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("provider down"));

        await Should.ThrowAsync<InvalidOperationException>(async () => await Sut().Handle(Command, Ct));
    }

    [Fact]
    public async Task No_signed_in_user_is_refused_before_any_budget_is_spent()
    {
        _currentUser.UserId.Returns((Guid?)null);

        var result = await Sut().Handle(Command, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.NotAuthenticated);
        await _budget.DidNotReceiveWithAnyArgs().TryConsumeAsync(default!, default!, Ct);
        await _accounts.DidNotReceiveWithAnyArgs().CheckAddressIsFreeAsync(default, default!, Ct);
    }

    [Theory]
    [InlineData(null, NewEmail)]
    [InlineData("", NewEmail)]
    [InlineData(Grant, null)]
    [InlineData(Grant, "")]
    public async Task Missing_input_is_refused_before_any_budget_is_spent(string? grant, string? newEmail)
    {
        var result = await Sut().Handle(new ChangeEmailCommand(grant, newEmail), Ct);

        result.IsFailure.ShouldBeTrue();
        await _budget.DidNotReceiveWithAnyArgs().TryConsumeAsync(default!, default!, Ct);
        await _store.DidNotReceiveWithAnyArgs().PutBoundAsync(default!, Ct);
    }

    [Fact]
    public async Task Every_request_gets_its_own_challenge_id()
    {
        var first = await Sut().Handle(Command, Ct);
        var second = await Sut().Handle(Command, Ct);

        first.Value.ChallengeId.ShouldNotBe(second.Value.ChallengeId);
    }

    [Fact]
    public void The_request_step_takes_no_session_store_and_no_grant_store()
    {
        // The swap and the logout-everywhere happen only at confirm, and a grant is issued only by the verify step.
        var parameters = typeof(ChangeEmailCommandHandler)
            .GetConstructors()
            .ShouldHaveSingleItem()
            .GetParameters()
            .Select(p => p.ParameterType)
            .ToList();

        parameters.ShouldNotContain(typeof(ISessionStore));
        parameters.ShouldNotContain(typeof(Jobbliggaren.Application.Auth.Grants.IGrantStore));
    }
}
