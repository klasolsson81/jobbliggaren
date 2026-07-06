using Jobbliggaren.Application.Auth.Commands.ConfirmEmailChange;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// #679 CONFIRM step — pins the handler's security invariants. The OLD address is captured BEFORE the
/// swap so the "your email was changed" notice reaches the previous owner (CTO-bind #4 / OWASP ASVS
/// V2.5); a confirm rejection propagates the ONE uniform error unchanged (no account/enum oracle) and
/// sends nothing; and the notice is strictly best-effort — a send failure must never fail (or roll
/// back) a completed change, and a null old address simply skips the notice.
/// </summary>
public class ConfirmEmailChangeCommandHandlerTests
{
    private const string OldEmail = "gammal.adress@example.se";
    private const string NewEmail = "ny.adress@example.se";
    private const string UrlSafeToken = "opaque-url-safe-token"; // gitleaks:allow

    private readonly IUserAccountService _service = Substitute.For<IUserAccountService>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();

    private ConfirmEmailChangeCommandHandler CreateHandler()
        => new(_service, _emailSender, NullLogger<ConfirmEmailChangeCommandHandler>.Instance);

    private static ConfirmEmailChangeCommand Command(Guid userId)
        => new(userId, NewEmail, UrlSafeToken);

    [Fact]
    public async Task Handle_WithValidToken_ConfirmsAndNotifiesOldAddress()
    {
        var userId = Guid.NewGuid();
        _service.GetEmailAsync(userId, Arg.Any<CancellationToken>()).Returns(OldEmail);
        _service.ConfirmChangeEmailAsync(userId, NewEmail, UrlSafeToken, Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        var handler = CreateHandler();

        var result = await handler.Handle(Command(userId), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        // The target user id: the User.EmailChanged audit aggregate id AND the id the endpoint
        // invalidates all sessions for (C6).
        result.Value.ShouldBe(userId);

        // The security notice goes to the OLD address captured before the swap, never the new one.
        await _emailSender.Received(1).SendEmailChangedNotificationAsync(
            OldEmail, Arg.Any<EmailChangedNotificationIdempotencyKey>(), Arg.Any<CancellationToken>());
        await _emailSender.DidNotReceive().SendEmailChangedNotificationAsync(
            NewEmail, Arg.Any<EmailChangedNotificationIdempotencyKey>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenConfirmFails_PropagatesUniformErrorWithoutNotifying()
    {
        var userId = Guid.NewGuid();
        _service.GetEmailAsync(userId, Arg.Any<CancellationToken>()).Returns(OldEmail);
        _service.ConfirmChangeEmailAsync(userId, NewEmail, UrlSafeToken, Arg.Any<CancellationToken>())
            .Returns(Result.Failure(DomainError.Validation(
                "Auth.InvalidEmailChangeToken", "Bekräftelselänken är ogiltig eller har gått ut.")));
        var handler = CreateHandler();

        var result = await handler.Handle(Command(userId), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Auth.InvalidEmailChangeToken");
        // A failed confirm sends no notice (nothing changed to notify about).
        await _emailSender.DidNotReceive().SendEmailChangedNotificationAsync(
            Arg.Any<string>(), Arg.Any<EmailChangedNotificationIdempotencyKey>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenOldAddressNoticeThrows_StillSucceeds()
    {
        var userId = Guid.NewGuid();
        _service.GetEmailAsync(userId, Arg.Any<CancellationToken>()).Returns(OldEmail);
        _service.ConfirmChangeEmailAsync(userId, NewEmail, UrlSafeToken, Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _emailSender.SendEmailChangedNotificationAsync(
                OldEmail, Arg.Any<EmailChangedNotificationIdempotencyKey>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("e-post-transport nere")));
        var handler = CreateHandler();

        var result = await handler.Handle(Command(userId), CancellationToken.None);

        // The change is committed; a best-effort notice failure is swallowed (logged) and never fails
        // or rolls back the completed change — the recovery vector already moved.
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(userId);
    }

    [Fact]
    public async Task Handle_WhenOldEmailIsNull_SucceedsWithoutNotifying()
    {
        var userId = Guid.NewGuid();
        _service.GetEmailAsync(userId, Arg.Any<CancellationToken>()).Returns((string?)null);
        _service.ConfirmChangeEmailAsync(userId, NewEmail, UrlSafeToken, Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        var handler = CreateHandler();

        var result = await handler.Handle(Command(userId), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        // A null old address (user already gone from Identity's view) skips the notice — no throw.
        await _emailSender.DidNotReceive().SendEmailChangedNotificationAsync(
            Arg.Any<string>(), Arg.Any<EmailChangedNotificationIdempotencyKey>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithEmptyUserId_ReturnsFailureWithoutConfirming()
    {
        var handler = CreateHandler();

        var result = await handler.Handle(
            new ConfirmEmailChangeCommand(Guid.Empty, NewEmail, UrlSafeToken), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Auth.InvalidInput");
        await _service.DidNotReceive().ConfirmChangeEmailAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Handle_WithMissingToken_ReturnsFailureWithoutConfirming(string? token)
    {
        var userId = Guid.NewGuid();
        var handler = CreateHandler();

        var result = await handler.Handle(
            new ConfirmEmailChangeCommand(userId, NewEmail, token), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Auth.InvalidInput");
        await _service.DidNotReceive().ConfirmChangeEmailAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
