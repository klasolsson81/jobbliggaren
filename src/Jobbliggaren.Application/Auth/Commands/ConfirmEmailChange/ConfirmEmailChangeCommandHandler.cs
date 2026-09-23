using Jobbliggaren.Application.Auth.Grants;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Application.Auth.Commands.ConfirmEmailChange;

/// <summary>
/// #1739 — the change-email confirm step (ADR 0142 D5): the grant is redeemed for this user and this address, the
/// store asserting both, and only then is the account moved. The old address is told after a successful swap.
/// </summary>
public sealed partial class ConfirmEmailChangeCommandHandler(
    ICurrentUser currentUser,
    IGrantStore grants,
    IUserAccountService userAccountService,
    IEmailSender emailSender,
    ILogger<ConfirmEmailChangeCommandHandler> logger)
    : ICommandHandler<ConfirmEmailChangeCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(ConfirmEmailChangeCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return Result.Failure<Guid>(
                DomainError.Validation(AuthErrorCodes.NotAuthenticated, "Inloggning krävs för att byta e-postadress."));

        // The validator guarantees both are non-empty; re-assert so the handler is correct in isolation.
        if (string.IsNullOrEmpty(command.ChangeEmailGrant) || string.IsNullOrEmpty(command.NewEmail))
            return Result.Failure<Guid>(
                DomainError.Validation(AuthErrorCodes.InvalidInput, "Ny e-postadress krävs."));

        var userId = currentUser.UserId.Value;

        // The store asserts the user AND the address (ADR 0142 D3): a grant for another user, another address,
        // another purpose, expired or already used is one answer. The redemption is single use either way.
        var subject = await grants.RedeemAsync(
            GrantToken.FromRaw(command.ChangeEmailGrant),
            GrantAssertion.Of(new GrantSubject.ChangeEmail(userId, command.NewEmail)),
            cancellationToken);
        if (subject is null)
            return Result.Failure<Guid>(DomainError.Gone(
                AuthErrorCodes.EmailChangeGrantUnusable, AuthErrorCodes.EmailChangeGrantUnusableMessage));

        // Captured BEFORE the swap so the security notice can reach it (CTO-bind #4).
        var oldEmail = await userAccountService.GetEmailAsync(userId, cancellationToken);

        var swapped = await userAccountService.SwapConfirmedAddressAsync(userId, command.NewEmail, cancellationToken);
        if (swapped.IsFailure)
            return Result.Failure<Guid>(swapped.Error);

        // Old-address security notice (CTO-bind #4): "your email was changed", so the previous owner can detect an
        // unauthorized change (OWASP ASVS V2.5 / NIST SP 800-63B). Best-effort, log-and-continue — a send failure
        // must never fail a completed change. No link, and it does not reveal the new address.
        if (!string.IsNullOrEmpty(oldEmail))
        {
            try
            {
                await emailSender.SendEmailChangedNotificationAsync(oldEmail, cancellationToken);
            }
            catch (Exception ex)
            {
                // §5 parity with the sender boundary: log only the exception TYPE and the opaque user id.
                LogOldAddressNotificationFailed(ex.GetType().Name, userId);
            }
        }

        // The User.EmailChanged audit aggregate id AND the id the endpoint re-issues the session for.
        return Result.Success(userId);
    }

    [LoggerMessage(4002, LogLevel.Warning,
        "Change-email confirm: old-address notification failed for user {UserId} ({ErrorType}) " +
        "(change succeeded)")]
    private partial void LogOldAddressNotificationFailed(string errorType, Guid userId);
}
