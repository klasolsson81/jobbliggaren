using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Commands.ChangeEmail;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Microsoft.Extensions.Options;
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
/// <para>
/// #703 — the per-user AND per-target anti-email-bomb cooldown gates the whole request BEFORE the
/// uniqueness pre-check: the per-user scope is checked first (short-circuit), then the per-target scope; a
/// cooled request is a VISIBLE 409 (<c>Auth.ChangeEmailCooldown</c>) and mints/sends nothing. The default
/// gate here is NOT cooling so the pre-#703 behavioural tests are unchanged.
/// </para>
/// </summary>
public class ChangeEmailCommandHandlerTests
{
    private const string CurrentPassword = "Current123456";
    private const string NewEmail = "ny.adress@example.se";
    private const string UrlSafeToken = "opaque-url-safe-token"; // gitleaks:allow

    private readonly IUserAccountService _service = Substitute.For<IUserAccountService>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly ICooldownGate _cooldown = Substitute.For<ICooldownGate>();
    private readonly IOptions<AuthEmailCooldownOptions> _cooldownOptions =
        Options.Create(new AuthEmailCooldownOptions());

    public ChangeEmailCommandHandlerTests()
    {
        // Default: NOT cooling — the behavioural tests below assert the send path, so both cooldown checks
        // must pass unless a test explicitly cools one scope.
        _cooldown.TryBeginAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(true);
    }

    private ChangeEmailCommandHandler CreateHandler(ICurrentUser currentUser)
        => new(currentUser, _service, _emailSender, _cooldown, _cooldownOptions);

    private static ICurrentUser AuthenticatedUser(Guid userId)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        return currentUser;
    }

    // Cool a specific scope (a later, more-specific NSubstitute setup wins over the ctor default).
    private void Cool(string scope) =>
        _cooldown.TryBeginAsync(scope, Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(false);

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
    public async Task Handle_WhenUserCoolingDown_Returns409_ShortCircuitsBeforeUniquenessCheck()
    {
        // #703: the per-USER (actor) throttle is checked FIRST. A cooled actor is a visible 409 and never
        // reaches the uniqueness pre-check, never mints a token, never emails, and never begins the victim
        // (per-target) window (short-circuit — a blocked actor must not extend a victim's window).
        var userId = Guid.NewGuid();
        Cool(CooldownScopes.ChangeEmailUser);
        var handler = CreateHandler(AuthenticatedUser(userId));

        var result = await handler.Handle(new ChangeEmailCommand(CurrentPassword, NewEmail), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.ChangeEmailCooldown);
        await _cooldown.DidNotReceive().TryBeginAsync(
            CooldownScopes.ChangeEmailTarget, Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
        await _service.DidNotReceive().IsEmailTakenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _service.DidNotReceive().GenerateChangeEmailTokenAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _emailSender.DidNotReceive().SendEmailChangeConfirmationAsync(
            Arg.Any<string>(), Arg.Any<EmailChangeConfirmationEmail>(),
            Arg.Any<EmailChangeConfirmationIdempotencyKey>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenTargetCoolingDown_Returns409_WithoutMintingOrSending()
    {
        // #703: with the actor fresh, a cooled TARGET (victim address) is still a visible 409 that mints
        // and sends nothing — the per-target throttle protects a victim inbox from an authenticated bomber.
        var userId = Guid.NewGuid();
        Cool(CooldownScopes.ChangeEmailTarget);
        var handler = CreateHandler(AuthenticatedUser(userId));

        var result = await handler.Handle(new ChangeEmailCommand(CurrentPassword, NewEmail), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.ChangeEmailCooldown);
        await _service.DidNotReceive().IsEmailTakenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _service.DidNotReceive().GenerateChangeEmailTokenAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _emailSender.DidNotReceive().SendEmailChangeConfirmationAsync(
            Arg.Any<string>(), Arg.Any<EmailChangeConfirmationEmail>(),
            Arg.Any<EmailChangeConfirmationIdempotencyKey>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenNotCooling_ChecksUserScopeThenTargetScope()
    {
        // Pins the per-user-AND-per-target design + the user-first order: both scopes are begun with the
        // authenticated user id and the new address respectively, then the request proceeds.
        var userId = Guid.NewGuid();
        _service.IsEmailTakenAsync(NewEmail, Arg.Any<CancellationToken>()).Returns(false);
        _service.GenerateChangeEmailTokenAsync(userId, NewEmail, Arg.Any<CancellationToken>())
            .Returns(Result.Success(UrlSafeToken));
        var handler = CreateHandler(AuthenticatedUser(userId));

        (await handler.Handle(new ChangeEmailCommand(CurrentPassword, NewEmail), CancellationToken.None))
            .IsSuccess.ShouldBeTrue();

        await _cooldown.Received(1).TryBeginAsync(
            CooldownScopes.ChangeEmailUser, userId.ToString(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
        await _cooldown.Received(1).TryBeginAsync(
            CooldownScopes.ChangeEmailTarget, NewEmail, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
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
