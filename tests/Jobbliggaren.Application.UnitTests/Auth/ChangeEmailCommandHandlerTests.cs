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
    // A distinct (non-default) window so the tests can pin that the handler reads
    // ChangeEmailWindowSeconds specifically (a copy-paste swap with AccountExistsNoticeWindowSeconds would
    // be invisible if both were the 60s default).
    private const int ChangeEmailWindowSeconds = 137;
    private readonly IOptions<AuthEmailCooldownOptions> _cooldownOptions =
        Options.Create(new AuthEmailCooldownOptions { ChangeEmailWindowSeconds = ChangeEmailWindowSeconds });

    public ChangeEmailCommandHandlerTests()
    {
        // Default: NOT cooling — the behavioural tests below assert the send path, so both cooldown checks
        // must pass unless a test explicitly cools one scope.
        _cooldown.TryBeginAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // #1087 — the capability gate is OPEN by default so every other test keeps failing for ITS OWN
        // cause. NSubstitute returns default(bool) = false for an unconfigured property, and the gate is
        // the handler's first check after self-defence, so without this line it would pre-empt every
        // cooldown / email-taken / token-failure case below.
        //
        // MEASURED, because the size of that hazard was itself asserted wrongly and the difference
        // matters. dotnet-architect (2026-08-09) graded it Viktigt on the premise that the five
        // DidNotReceive() assertions "would ALL still pass", silently repurposed. Run against this
        // fixture with the line removed, 6 of 12 FAIL: four on result.Error.Code (each test pins its own
        // cause — UserNotFound, ChangeEmailCooldown ×2, EmailTaken), one on result.IsSuccess, one on the
        // ordering case's success assertion. None of them went quietly. The remedy below is still
        // required; what is NOT true is that this fixture would have hidden the change. It does not rely
        // on DidNotReceive() alone anywhere — every negative assertion has a positive sister in the same
        // test, which is exactly what makes a negated assertion able to fail at all.
        _emailSender.CanDeliver.Returns(true);
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
            Arg.Is<EmailChangeConfirmationEmail>(c => c != null &&
                c.UserId == userId && c.NewEmail == NewEmail && c.UrlSafeToken == UrlSafeToken),
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
            Arg.Any<CancellationToken>());
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
            Arg.Any<CancellationToken>());
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
        // The auth guard runs BEFORE the cooldown: an unauthenticated request must never begin a window
        // (it has no userId, and it must not burn a victim's per-target window).
        await _cooldown.DidNotReceive().TryBeginAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
        await _service.DidNotReceive().IsEmailTakenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _service.DidNotReceive().GenerateChangeEmailTokenAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _emailSender.DidNotReceive().SendEmailChangeConfirmationAsync(
            Arg.Any<string>(), Arg.Any<EmailChangeConfirmationEmail>(),
            Arg.Any<CancellationToken>());
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
        // The input guard runs BEFORE the cooldown, so a null/empty new email never reaches the gate — this
        // also guarantees no NRE on subject.Trim() for a null NewEmail, and no window is burned on bad input.
        await _cooldown.DidNotReceive().TryBeginAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
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
            Arg.Any<CancellationToken>());
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
            Arg.Any<CancellationToken>());
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

        // Both scopes are begun with the ChangeEmailWindowSeconds window (pins the options-property source —
        // a swap with AccountExistsNoticeWindowSeconds would be caught here).
        await _cooldown.Received(1).TryBeginAsync(
            CooldownScopes.ChangeEmailUser, userId.ToString(),
            TimeSpan.FromSeconds(ChangeEmailWindowSeconds), Arg.Any<CancellationToken>());
        await _cooldown.Received(1).TryBeginAsync(
            CooldownScopes.ChangeEmailTarget, NewEmail,
            TimeSpan.FromSeconds(ChangeEmailWindowSeconds), Arg.Any<CancellationToken>());
    }

    // ---------------------------------------------------------------------------------------
    // #1087 — the capability gate, pinned at the CALL SITE and as a PAIR.
    //
    // AC 4 asks for the call site rather than the rule, and the reason is exact: a rule test
    // (NullEmailSender.CanDeliver is false) stays green forever while this handler never consults
    // it. Only a test that drives THIS handler can tell a live gate from a dead one.
    //
    // The pair is not tidiness either. The refusal half is built from negated assertions
    // (DidNotReceive, no token minted), and a negated assertion cannot fail its own pattern — delete
    // the gate from the handler and the sends happen, which the negatives catch, but delete the
    // handler's whole body and they pass just as well. The capability-TRUE sister is what proves the
    // branch is a choice: same fixture, same command, exactly one input flipped.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Handle_WhenSenderCannotDeliver_RefusesBeforeMintingOrSending()
    {
        var userId = Guid.NewGuid();
        _emailSender.CanDeliver.Returns(false);
        _service.IsEmailTakenAsync(NewEmail, Arg.Any<CancellationToken>()).Returns(false);
        _service.GenerateChangeEmailTokenAsync(userId, NewEmail, Arg.Any<CancellationToken>())
            .Returns(Result.Success(UrlSafeToken));
        var handler = CreateHandler(AuthenticatedUser(userId));

        var result = await handler.Handle(new ChangeEmailCommand(CurrentPassword, NewEmail), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.EmailDeliveryUnavailable);

        // No token is minted for a request that cannot complete — the credential would be a live
        // change-email token nobody can ever receive.
        await _service.DidNotReceive().GenerateChangeEmailTokenAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _emailSender.DidNotReceive().SendEmailChangeConfirmationAsync(
            Arg.Any<string>(), Arg.Any<EmailChangeConfirmationEmail>(), Arg.Any<CancellationToken>());

        // AC 3: no audit row. AuditBehavior stamps only on Result.Success, so the refusal discharges
        // this by construction — pinned here so a later change to that behavior is caught at the site
        // that depends on it, not only where the behavior lives.
        result.IsSuccess.ShouldBeFalse();

        // The actor's 60s anti-email-bomb window is NOT consumed: the gate reads no request input and
        // runs ahead of the cooldown, so a server-side misconfiguration cannot rate-limit the user out
        // of retrying once the provider is configured.
        await _cooldown.DidNotReceive().TryBeginAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenSenderCanDeliver_ProceedsToSend()
    {
        // The crossing sister of the test above: identical arrangement, CanDeliver flipped to true.
        // Without this the refusal test would pass against a handler that refuses unconditionally.
        var userId = Guid.NewGuid();
        _emailSender.CanDeliver.Returns(true);
        _service.IsEmailTakenAsync(NewEmail, Arg.Any<CancellationToken>()).Returns(false);
        _service.GenerateChangeEmailTokenAsync(userId, NewEmail, Arg.Any<CancellationToken>())
            .Returns(Result.Success(UrlSafeToken));
        var handler = CreateHandler(AuthenticatedUser(userId));

        var result = await handler.Handle(new ChangeEmailCommand(CurrentPassword, NewEmail), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        await _emailSender.Received(1).SendEmailChangeConfirmationAsync(
            NewEmail, Arg.Any<EmailChangeConfirmationEmail>(), Arg.Any<CancellationToken>());
        await _cooldown.Received(2).TryBeginAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
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
