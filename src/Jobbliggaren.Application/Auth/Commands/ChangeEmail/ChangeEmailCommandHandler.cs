using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Application.Auth.Commands.ChangeEmail;

public sealed class ChangeEmailCommandHandler(
    ICurrentUser currentUser,
    IUserAccountService userAccountService,
    IEmailSender emailSender,
    ICooldownGate cooldown,
    IOptions<AuthEmailCooldownOptions> cooldownOptions)
    : ICommandHandler<ChangeEmailCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(ChangeEmailCommand command, CancellationToken cancellationToken)
    {
        // Self-defending (mirrors ChangePassword / DeleteAccount): Authorization + Reauthentication ran
        // before this handler, but we do not take a dependency on pipeline configuration.
        if (!currentUser.UserId.HasValue)
            return Result.Failure<Guid>(
                DomainError.Validation("Auth.NotAuthenticated", "Inloggning krävs för att byta e-postadress."));

        // The validator guarantees both are non-empty; re-assert so the handler is correct in isolation.
        if (string.IsNullOrEmpty(command.CurrentPassword) || string.IsNullOrEmpty(command.NewEmail))
            return Result.Failure<Guid>(
                DomainError.Validation("Auth.InvalidInput", "Nuvarande lösenord och ny e-postadress krävs."));

        var userId = currentUser.UserId.Value;
        var newEmail = command.NewEmail;

        // #703: per-user AND per-target anti-email-bomb cooldown. Each request mints a fresh token and mails
        // an attacker-chosen NewEmail; the per-IP AuthWrite limit protects the attacker's bucket, not the
        // victim inbox. Check per-USER first (the actor throttle is primary — short-circuit so a throttled
        // actor cannot also extend a victim's window), then per-TARGET. A cooled request is a VISIBLE 409
        // (unlike the unauthenticated resend / account-exists silent no-op): this path is authenticated and
        // already leaks existence via the EmailTaken 409, so anti-enum silence buys nothing and a "wait a
        // moment" beats a false "link sent". A rejected request consumes the actor's window — the correct
        // anti-retry behaviour for a rare 60s action.
        var window = TimeSpan.FromSeconds(cooldownOptions.Value.ChangeEmailWindowSeconds);
        if (!await cooldown.TryBeginAsync(CooldownScopes.ChangeEmailUser, userId.ToString(), window, cancellationToken)
            || !await cooldown.TryBeginAsync(CooldownScopes.ChangeEmailTarget, newEmail, window, cancellationToken))
        {
            return Result.Failure<Guid>(
                DomainError.Conflict(AuthErrorCodes.ChangeEmailCooldown, AuthErrorCodes.ChangeEmailCooldownMessage));
        }

        // Request-time uniqueness pre-check (Klas: a clear 409 "adressen är upptagen"). Only an
        // authenticated + re-authenticated user reaches here (ReauthenticationBehavior ran first), so
        // this enumeration surface is far narrower than registration's unauthenticated 400 leak;
        // uniqueness is still enforced authoritatively at confirm (ConfirmChangeEmailAsync, TOCTOU).
        if (await userAccountService.IsEmailTakenAsync(newEmail, cancellationToken))
            return Result.Failure<Guid>(
                DomainError.Conflict("Auth.EmailTaken", "Den e-postadressen är upptagen."));

        // Mint the opaque, URL-safe ownership-confirmation token bound to (user, newEmail). The email
        // is NOT changed here — the pending state lives entirely in the emailed link.
        var tokenResult = await userAccountService.GenerateChangeEmailTokenAsync(userId, newEmail, cancellationToken);
        if (tokenResult.IsFailure)
            return Result.Failure<Guid>(tokenResult.Error);

        var urlSafeToken = tokenResult.Value;

        // Send the confirmation link to the NEW address. A send failure propagates (no User.
        // EmailChangeRequested audit row is written — AuditBehavior only stamps on Result.Success).
        await emailSender.SendEmailChangeConfirmationAsync(
            newEmail,
            new EmailChangeConfirmationEmail(userId, newEmail, urlSafeToken),
            EmailChangeConfirmationIdempotencyKey.For(userId, urlSafeToken),
            cancellationToken);

        // Return the authenticated user id: the User.EmailChangeRequested audit aggregate id. The
        // email is unchanged and no session is touched (the swap happens only at confirm).
        return Result.Success(userId);
    }
}
