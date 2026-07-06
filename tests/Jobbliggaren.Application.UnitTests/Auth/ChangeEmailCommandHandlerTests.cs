using Jobbliggaren.Application.Auth.Commands.ChangeEmail;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// #679 REQUEST step — pins the handler's security invariants (parity with
/// <c>ChangePasswordCommandHandlerTests</c>): self-defends against a missing principal; a request-time
/// uniqueness pre-check GATES token minting (a taken address is a 409 and never mints a token or emails
/// anyone); a token-gen failure propagates without a send; and on success the ownership-confirmation
/// link is emailed to the NEW address carrying (userId, newEmail, token), returning the authenticated
/// user id for the <c>User.EmailChangeRequested</c> audit. The request step must NEVER touch sessions
/// (the swap + logout-everywhere happens only at confirm) — pinned structurally.
/// </summary>
public class ChangeEmailCommandHandlerTests
{
    private const string CurrentPassword = "Current123456";
    private const string NewEmail = "ny.adress@example.se";
    private const string UrlSafeToken = "opaque-url-safe-token"; // gitleaks:allow

    private readonly IUserAccountService _service = Substitute.For<IUserAccountService>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();

    private ChangeEmailCommandHandler CreateHandler(ICurrentUser currentUser)
        => new(currentUser, _service, _emailSender);

    private static ICurrentUser AuthenticatedUser(Guid userId)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        return currentUser;
    }

    [Fact]
    public async Task Handle_WithValidChange_EmailsConfirmationToNewAddressAndReturnsUserId()
    {
        var userId = Guid.NewGuid();
        _service.IsEmailTakenAsync(NewEmail, Arg.Any<CancellationToken>()).Returns(false);
        _service.GenerateChangeEmailTokenAsync(userId, NewEmail, Arg.Any<CancellationToken>())
            .Returns(Result.Success(UrlSafeToken));
        var handler = CreateHandler(AuthenticatedUser(userId));

        var result = await handler.Handle(new ChangeEmailCommand(CurrentPassword, NewEmail), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        // The User.EmailChangeRequested audit aggregate id — the authenticated user id.
        result.Value.ShouldBe(userId);

        // The uniqueness pre-check ran, and the confirmation link went to the NEW address carrying
        // (userId, newEmail, token) so the template can build /bekrafta-epost?uid=&email=&token=.
        await _service.Received(1).IsEmailTakenAsync(NewEmail, Arg.Any<CancellationToken>());
        await _emailSender.Received(1).SendEmailChangeConfirmationAsync(
            NewEmail,
            Arg.Is<EmailChangeConfirmationEmail>(c =>
                c.UserId == userId && c.NewEmail == NewEmail && c.UrlSafeToken == UrlSafeToken),
            Arg.Any<EmailChangeConfirmationIdempotencyKey>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenEmailTaken_ReturnsConflictWithoutMintingTokenOrSending()
    {
        var userId = Guid.NewGuid();
        _service.IsEmailTakenAsync(NewEmail, Arg.Any<CancellationToken>()).Returns(true);
        var handler = CreateHandler(AuthenticatedUser(userId));

        var result = await handler.Handle(new ChangeEmailCommand(CurrentPassword, NewEmail), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Auth.EmailTaken");
        // The uniqueness check gates token minting: a taken address never mints a token or emails anyone.
        await _service.DidNotReceive().GenerateChangeEmailTokenAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _emailSender.DidNotReceive().SendEmailChangeConfirmationAsync(
            Arg.Any<string>(), Arg.Any<EmailChangeConfirmationEmail>(),
            Arg.Any<EmailChangeConfirmationIdempotencyKey>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenTokenGenerationFails_PropagatesErrorWithoutSending()
    {
        var userId = Guid.NewGuid();
        _service.IsEmailTakenAsync(NewEmail, Arg.Any<CancellationToken>()).Returns(false);
        _service.GenerateChangeEmailTokenAsync(userId, NewEmail, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<string>(DomainError.NotFound("Auth.UserNotFound", "Användaren hittades inte.")));
        var handler = CreateHandler(AuthenticatedUser(userId));

        var result = await handler.Handle(new ChangeEmailCommand(CurrentPassword, NewEmail), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Auth.UserNotFound");
        await _emailSender.DidNotReceive().SendEmailChangeConfirmationAsync(
            Arg.Any<string>(), Arg.Any<EmailChangeConfirmationEmail>(),
            Arg.Any<EmailChangeConfirmationIdempotencyKey>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenUnauthenticated_ReturnsFailureWithoutTouchingServices()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);
        var handler = CreateHandler(currentUser);

        var result = await handler.Handle(new ChangeEmailCommand(CurrentPassword, NewEmail), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Auth.NotAuthenticated");
        await _service.DidNotReceive().IsEmailTakenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _service.DidNotReceive().GenerateChangeEmailTokenAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _emailSender.DidNotReceive().SendEmailChangeConfirmationAsync(
            Arg.Any<string>(), Arg.Any<EmailChangeConfirmationEmail>(),
            Arg.Any<EmailChangeConfirmationIdempotencyKey>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null, NewEmail)]
    [InlineData("", NewEmail)]
    [InlineData(CurrentPassword, null)]
    [InlineData(CurrentPassword, "")]
    public async Task Handle_WithMissingInput_ReturnsFailureWithoutMintingToken(string? current, string? newEmail)
    {
        var userId = Guid.NewGuid();
        var handler = CreateHandler(AuthenticatedUser(userId));

        var result = await handler.Handle(new ChangeEmailCommand(current, newEmail), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        await _service.DidNotReceive().GenerateChangeEmailTokenAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Handler_DoesNotDependOnSessionStore()
    {
        // The REQUEST step must NOT touch sessions — the email swap + C6 logout-everywhere happens
        // only at confirm. Pinned structurally so a future refactor can't quietly wire ISessionStore
        // into the request handler (which would log the user out before they own the new address).
        var parameterTypes = typeof(ChangeEmailCommandHandler)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(p => p.ParameterType);

        parameterTypes.ShouldNotContain(typeof(ISessionStore));
    }
}
