using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Commands.ConfirmEmailChange;
using Jobbliggaren.Application.Auth.Grants;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// The change-email confirm step (#679; a grant since #1739, ADR 0142 D5). The grant is redeemed with an assertion
/// of this user AND this address, and nothing is moved until it redeems. The old address is read before the swap
/// so the "your email was changed" notice reaches the previous owner (CTO-bind #4), and that notice is
/// best-effort: it never fails a completed change.
/// </summary>
public sealed class ConfirmEmailChangeCommandHandlerTests
{
    private const string OldEmail = "gammal.adress@example.se";
    private const string NewEmail = "ny.adress@example.se";
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly GrantToken Grant = GrantToken.Generate();

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IGrantStore _grants = Substitute.For<IGrantStore>();
    private readonly IUserAccountService _accounts = Substitute.For<IUserAccountService>();
    private readonly IEmailSender _sender = Substitute.For<IEmailSender>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static GrantAssertion Expected => GrantAssertion.Of(new GrantSubject.ChangeEmail(UserId, NewEmail));

    public ConfirmEmailChangeCommandHandlerTests()
    {
        _currentUser.UserId.Returns(UserId);
        _grants.RedeemAsync(Grant, Expected, Arg.Any<CancellationToken>())
            .Returns(new GrantSubject.ChangeEmail(UserId, NewEmail));
        _accounts.GetEmailAsync(UserId, Arg.Any<CancellationToken>()).Returns(OldEmail);
        _accounts.SwapConfirmedAddressAsync(UserId, NewEmail, Arg.Any<CancellationToken>()).Returns(Result.Success());
    }

    private ConfirmEmailChangeCommandHandler Sut() =>
        new(_currentUser, _grants, _accounts, _sender, NullLogger<ConfirmEmailChangeCommandHandler>.Instance);

    private static ConfirmEmailChangeCommand Command => new(Grant.Reveal(), NewEmail);

    [Fact]
    public async Task A_redeemed_grant_moves_the_account_and_tells_the_old_address()
    {
        var result = await Sut().Handle(Command, Ct);

        result.IsSuccess.ShouldBeTrue();
        // The User.EmailChanged audit aggregate id AND the id the endpoint re-issues the session for.
        result.Value.ShouldBe(UserId);
        Received.InOrder(async () =>
        {
            await _grants.RedeemAsync(Grant, Expected, Arg.Any<CancellationToken>());
            await _accounts.GetEmailAsync(UserId, Arg.Any<CancellationToken>());
            await _accounts.SwapConfirmedAddressAsync(UserId, NewEmail, Arg.Any<CancellationToken>());
            await _sender.SendEmailChangedNotificationAsync(OldEmail, Arg.Any<CancellationToken>());
        });
        await _sender.DidNotReceive().SendEmailChangedNotificationAsync(NewEmail, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_grant_is_asserted_for_the_sessions_user_and_the_commands_address()
    {
        // The handler compares nothing itself: the assertion is the whole binding, and the store refuses anything
        // else as one answer.
        await Sut().Handle(Command, Ct);

        await _grants.Received(1).RedeemAsync(
            Grant,
            Arg.Is<GrantAssertion>(a => a == Expected),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unusable_grant_is_gone_and_moves_nothing()
    {
        _grants.RedeemAsync(Grant, Expected, Arg.Any<CancellationToken>()).Returns((GrantSubject?)null);

        var result = await Sut().Handle(Command, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.EmailChangeGrantUnusable);
        result.Error.Kind.ShouldBe(ErrorKind.Gone);
        await _accounts.DidNotReceiveWithAnyArgs().SwapConfirmedAddressAsync(default, default!, Ct);
        await _sender.DidNotReceiveWithAnyArgs().SendEmailChangedNotificationAsync(default!, Ct);
    }

    [Fact]
    public async Task A_refused_swap_propagates_its_error_and_notifies_nobody()
    {
        var taken = DomainError.Conflict(AuthErrorCodes.EmailTaken, AuthErrorCodes.EmailTakenMessage);
        _accounts.SwapConfirmedAddressAsync(UserId, NewEmail, Arg.Any<CancellationToken>()).Returns(Result.Failure(taken));

        var result = await Sut().Handle(Command, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(taken);
        await _sender.DidNotReceiveWithAnyArgs().SendEmailChangedNotificationAsync(default!, Ct);
    }

    [Fact]
    public async Task A_notice_that_throws_never_fails_the_completed_change()
    {
        _sender.SendEmailChangedNotificationAsync(OldEmail, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("e-post-transport nere")));

        var result = await Sut().Handle(Command, Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(UserId);
    }

    [Fact]
    public async Task No_old_address_skips_the_notice()
    {
        _accounts.GetEmailAsync(UserId, Arg.Any<CancellationToken>()).Returns((string?)null);

        var result = await Sut().Handle(Command, Ct);

        result.IsSuccess.ShouldBeTrue();
        await _sender.DidNotReceiveWithAnyArgs().SendEmailChangedNotificationAsync(default!, Ct);
    }

    [Fact]
    public async Task No_signed_in_user_is_refused_before_the_grant_is_redeemed()
    {
        _currentUser.UserId.Returns((Guid?)null);

        var result = await Sut().Handle(Command, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.NotAuthenticated);
        await _grants.DidNotReceiveWithAnyArgs().RedeemAsync(default, default!, Ct);
        await _accounts.DidNotReceiveWithAnyArgs().SwapConfirmedAddressAsync(default, default!, Ct);
    }

    [Theory]
    [InlineData(null, NewEmail)]
    [InlineData("", NewEmail)]
    [InlineData("a-grant", null)]
    [InlineData("a-grant", "")]
    public async Task Missing_input_is_refused_before_the_grant_is_redeemed(string? grant, string? newEmail)
    {
        var result = await Sut().Handle(new ConfirmEmailChangeCommand(grant, newEmail), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Auth.InvalidInput");
        await _grants.DidNotReceiveWithAnyArgs().RedeemAsync(default, default!, Ct);
        await _accounts.DidNotReceiveWithAnyArgs().SwapConfirmedAddressAsync(default, default!, Ct);
    }
}
