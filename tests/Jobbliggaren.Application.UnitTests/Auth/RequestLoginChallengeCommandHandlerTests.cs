using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Commands.RequestLoginChallenge;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// The login challenge's request path (ADR 0142 D2). Its answer may not depend on the address beyond its
/// format, so the tests pin the ORDER of the gates, that a refused gate spends nothing further, and — by the
/// constructor — that nothing on this path can read an account.
/// </summary>
public sealed class RequestLoginChallengeCommandHandlerTests
{
    private const string Email = "person@example.com";

    private readonly IEmailSender _sender = Substitute.For<IEmailSender>();
    private readonly IRateBudget _budget = Substitute.For<IRateBudget>();
    private readonly ILoginChallengeDispatcher _dispatcher = Substitute.For<ILoginChallengeDispatcher>();
    private readonly IRequestContextProvider _context = Substitute.For<IRequestContextProvider>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public RequestLoginChallengeCommandHandlerTests()
    {
        _sender.CanDeliver.Returns(true);
        _context.IpAddress.Returns("203.0.113.0");
        _context.UserAgent.Returns("probe/1.0");
        Admit(cooldown: true, mails: true, codes: true);
    }

    private void Admit(bool cooldown, bool mails, bool codes)
    {
        _budget.TryConsumeAsync(Arg.Is<RateBudgetScope>(s => s.Name == "login-challenge-cooldown"), Email, Arg.Any<CancellationToken>())
            .Returns(cooldown);
        _budget.TryConsumeAsync(LoginChallengePolicy.MailBudget, Email, Arg.Any<CancellationToken>()).Returns(mails);
        _budget.TryConsumeAsync(LoginChallengePolicy.CodeBudget, Email, Arg.Any<CancellationToken>()).Returns(codes);
    }

    private RequestLoginChallengeCommandHandler Sut(int cooldownSeconds = 60) => new(
        _sender,
        _budget,
        Options.Create(new AuthEmailCooldownOptions { LoginChallengeWindowSeconds = cooldownSeconds }),
        _dispatcher,
        _context);

    [Fact]
    public async Task The_cooldown_is_one_call_per_configured_window()
    {
        await Sut(cooldownSeconds: 90).Handle(new RequestLoginChallengeCommand(Email), Ct);

        await _budget.Received(1).TryConsumeAsync(
            Arg.Is<RateBudgetScope>(s => s.Name == "login-challenge-cooldown" && s.Limit == 1
                && s.Window == TimeSpan.FromSeconds(90)),
            Email,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_sender_that_cannot_deliver_refuses_first_and_spends_no_budget()
    {
        _sender.CanDeliver.Returns(false);

        var result = await Sut().Handle(new RequestLoginChallengeCommand(Email), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.EmailDeliveryUnavailable);
        await _budget.DidNotReceiveWithAnyArgs().TryConsumeAsync(default!, default!, Ct);
        _dispatcher.DidNotReceiveWithAnyArgs().Enqueue(default!);
    }

    [Fact]
    public async Task An_admitted_request_passes_cooldown_mail_budget_and_code_budget_in_that_order_then_enqueues()
    {
        var result = await Sut().Handle(new RequestLoginChallengeCommand(Email), Ct);

        result.IsSuccess.ShouldBeTrue();
        Received.InOrder(() =>
        {
            _budget.TryConsumeAsync(Arg.Is<RateBudgetScope>(s => s.Name == "login-challenge-cooldown"), Email, Arg.Any<CancellationToken>());
            _budget.TryConsumeAsync(LoginChallengePolicy.MailBudget, Email, Arg.Any<CancellationToken>());
            _budget.TryConsumeAsync(LoginChallengePolicy.CodeBudget, Email, Arg.Any<CancellationToken>());
            _dispatcher.Enqueue(Arg.Any<LoginChallengeDispatch>());
        });
        _dispatcher.Received(1).Enqueue(new LoginChallengeDispatch(
            result.Value, Email, CodeBudgetState.Admitted, "203.0.113.0", "probe/1.0"));
    }

    [Fact]
    public async Task A_cooled_request_answers_the_same_success_and_spends_no_mail_or_code_budget()
    {
        Admit(cooldown: false, mails: true, codes: true);

        var result = await Sut().Handle(new RequestLoginChallengeCommand(Email), Ct);

        result.IsSuccess.ShouldBeTrue();
        await _budget.DidNotReceive().TryConsumeAsync(LoginChallengePolicy.MailBudget, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _budget.DidNotReceive().TryConsumeAsync(LoginChallengePolicy.CodeBudget, Arg.Any<string>(), Arg.Any<CancellationToken>());
        _dispatcher.DidNotReceiveWithAnyArgs().Enqueue(default!);
    }

    [Fact]
    public async Task A_request_over_the_mail_budget_answers_the_same_success_and_spends_no_code_budget()
    {
        Admit(cooldown: true, mails: false, codes: true);

        var result = await Sut().Handle(new RequestLoginChallengeCommand(Email), Ct);

        result.IsSuccess.ShouldBeTrue();
        await _budget.DidNotReceive().TryConsumeAsync(LoginChallengePolicy.CodeBudget, Arg.Any<string>(), Arg.Any<CancellationToken>());
        _dispatcher.DidNotReceiveWithAnyArgs().Enqueue(default!);
    }

    [Fact]
    public async Task A_request_over_the_code_budget_is_still_enqueued_marked_exhausted()
    {
        // The code budget refuses nothing: past it an existing account still gets a mail, with a link only.
        Admit(cooldown: true, mails: true, codes: false);

        var result = await Sut().Handle(new RequestLoginChallengeCommand(Email), Ct);

        result.IsSuccess.ShouldBeTrue();
        _dispatcher.Received(1).Enqueue(Arg.Is<LoginChallengeDispatch>(d => d.CodeBudget == CodeBudgetState.Exhausted));
    }

    [Fact]
    public async Task Every_request_gets_its_own_challenge_id()
    {
        var first = await Sut().Handle(new RequestLoginChallengeCommand(Email), Ct);
        var second = await Sut().Handle(new RequestLoginChallengeCommand(Email), Ct);

        first.Value.ShouldNotBe(second.Value);
    }

    [Fact]
    public void The_request_path_takes_nothing_that_can_read_an_account_or_a_challenge()
    {
        // The pin on ADR 0142 D2's "the request path never reads the account". A service locator inside an
        // async body would hide in a compiler-generated state machine, so the constructor is what is checked.
        var parameters = typeof(RequestLoginChallengeCommandHandler).GetConstructors().Single().GetParameters()
            .Select(p => p.ParameterType).ToList();

        Type[] forbidden =
        [
            typeof(IUserAccountService), typeof(ILoginAccountLookup), typeof(IAppDbContext),
            typeof(LoginSubjectResolver), typeof(ILoginChallengeStore), typeof(ISessionStore),
            typeof(IServiceProvider),
        ];
        parameters.Intersect(forbidden).ShouldBeEmpty();
    }
}
