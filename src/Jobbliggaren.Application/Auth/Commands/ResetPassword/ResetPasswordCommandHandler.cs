using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Application.Auth.Commands.ResetPassword;

public sealed partial class ResetPasswordCommandHandler(
    IUserAccountService userAccountService,
    IEmailSender emailSender,
    ILogger<ResetPasswordCommandHandler> logger)
    : ICommandHandler<ResetPasswordCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(
        ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        // Public / token-gated (no ICurrentUser): the URL-safe token IS the authorization. The validator
        // guarantees non-empty UserId/Token/NewPassword; re-assert so the handler is correct in isolation
        // (parity with ConfirmEmailChangeCommandHandler).
        if (command.UserId == Guid.Empty
            || string.IsNullOrEmpty(command.Token)
            || string.IsNullOrEmpty(command.NewPassword))
            return Result.Failure<Guid>(
                DomainError.Validation("Auth.InvalidInput", "Ogiltig återställningslänk."));

        // Capture the address BEFORE the reset so the security notice can reach it. The reset does not
        // change the address, but reading it first keeps the notice independent of anything the reset
        // does to the account, and mirrors the change-email confirm's ordering.
        var summary = await userAccountService.GetAccountSummaryAsync(command.UserId, cancellationToken);

        // Apply the reset: verify the token, run the password validators (HIBP + length), rotate the
        // stamp (single-use), clear any lockout. ONE uniform failure for every TOKEN rejection — no
        // oracle on a public endpoint — while a PASSWORD rejection names its rule, which is safe because
        // Identity verifies the token first and so that arm is reachable only with a valid token.
        var result = await userAccountService.ResetPasswordAsync(
            command.UserId, command.Token, command.NewPassword, cancellationToken);
        if (result.IsFailure)
            return Result.Failure<Guid>(result.Error);

        // Password-changed security notice, so a real owner can detect a reset they did not perform
        // (OWASP ASVS V2.5 / NIST SP 800-63B). Best-effort, log-and-continue: the password is already
        // changed and the token already spent, so failing here would strand the user on an error whose
        // retry reports "invalid link". Carries no token and no link that grants access.
        //
        // No CanDeliver branch, and that is closure rather than omission: no reset token can be minted
        // while the registered sender cannot deliver (RequestPasswordResetCommandHandler refuses first),
        // so with such a sender this line is unreachable rather than silently dropped — the same
        // trigger-unreachability argument security-auditor accepted 2026-08-09 for the old-address notice.
        if (summary?.Email is { Length: > 0 } address)
        {
            try
            {
                await emailSender.SendPasswordChangedNoticeAsync(address, cancellationToken);
            }
            catch (Exception ex)
            {
                // §5 parity with the sender boundary (ScalewayEmailSender logs only the type): a transport
                // exception can carry a host or status, never the recipient — so log the exception TYPE
                // plus the opaque userId surrogate, never the exception object or the address.
                LogPasswordChangedNoticeFailed(ex.GetType().Name, command.UserId);
            }
        }

        // The User.PasswordReset audit aggregate id AND the id the endpoint tears every session down for.
        return Result.Success(command.UserId);
    }

    [LoggerMessage(4005, LogLevel.Warning,
        "Password reset: changed-password notification failed for user {UserId} ({ErrorType}) " +
        "(reset succeeded)")]
    private partial void LogPasswordChangedNoticeFailed(string errorType, Guid userId);
}
