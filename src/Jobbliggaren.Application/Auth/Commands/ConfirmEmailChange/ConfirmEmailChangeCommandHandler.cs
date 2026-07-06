using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Application.Auth.Commands.ConfirmEmailChange;

public sealed partial class ConfirmEmailChangeCommandHandler(
    IUserAccountService userAccountService,
    IEmailSender emailSender,
    ILogger<ConfirmEmailChangeCommandHandler> logger)
    : ICommandHandler<ConfirmEmailChangeCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(ConfirmEmailChangeCommand command, CancellationToken cancellationToken)
    {
        // Public / token-gated (no ICurrentUser): the URL-safe token IS the authorization. The
        // validator guarantees non-empty UserId/NewEmail/Token; re-assert so the handler is correct
        // in isolation.
        if (command.UserId == Guid.Empty
            || string.IsNullOrEmpty(command.NewEmail)
            || string.IsNullOrEmpty(command.Token))
            return Result.Failure<Guid>(
                DomainError.Validation("Auth.InvalidInput", "Ogiltig bekräftelselänk."));

        // Capture the OLD address BEFORE the swap so the security notice can reach it (CTO-bind #4).
        // Null only if the user is already gone — the confirm below then fails uniformly anyway.
        var oldEmail = await userAccountService.GetEmailAsync(command.UserId, cancellationToken);

        // Apply the change: verify the token, swap Email/NormalizedEmail (+ EmailConfirmed), keep
        // UserName in lockstep, rotate the stamp (single-use). ONE uniform failure for every rejection
        // (user-not-found / bad-or-expired token / taken-at-confirm) — no oracle on a public endpoint.
        var result = await userAccountService.ConfirmChangeEmailAsync(
            command.UserId, command.NewEmail, command.Token, cancellationToken);
        if (result.IsFailure)
            return Result.Failure<Guid>(result.Error);

        // Old-address security notice (CTO-bind #4): "your email was changed", so the previous owner
        // can detect an unauthorized change (OWASP ASVS V2.5 / NIST SP 800-63B). Best-effort,
        // log-and-continue — a send failure must never fail (or roll back) a completed change. No
        // token/link, does not reveal the new address.
        if (!string.IsNullOrEmpty(oldEmail))
        {
            try
            {
                await emailSender.SendEmailChangedNotificationAsync(
                    oldEmail,
                    EmailChangedNotificationIdempotencyKey.For(command.UserId, command.Token),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                // §5 parity with the sender boundary (ResendEmailSender logs only the type): a
                // transport exception can carry a host/status, never the recipient/body/token — so log
                // only the exception TYPE + the opaque userId surrogate, not the exception object.
                LogOldAddressNotificationFailed(ex.GetType().Name, command.UserId);
            }
        }

        // Return the target user id: the User.EmailChanged audit aggregate id AND the id the endpoint
        // invalidates all sessions for (C6). A null actor (logged-out confirmer) is written correctly.
        return Result.Success(command.UserId);
    }

    [LoggerMessage(4002, LogLevel.Warning,
        "Change-email confirm: old-address notification failed for user {UserId} ({ErrorType}) " +
        "(change succeeded)")]
    private partial void LogOldAddressNotificationFailed(string errorType, Guid userId);
}
