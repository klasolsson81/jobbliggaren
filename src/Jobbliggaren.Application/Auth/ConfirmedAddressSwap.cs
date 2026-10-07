using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Application.Auth;

/// <summary>
/// Moves an account to an address whose inbox has been proven, then tells the address it moved from (ADR 0142 D5).
/// The one caller of <see cref="IUserAccountService.SwapConfirmedAddressAsync"/>, shared by every flow that completes
/// an address change, so the write order and the notice cannot drift apart between them.
/// </summary>
public sealed partial class ConfirmedAddressSwap(
    IUserAccountService userAccountService,
    IEmailSender emailSender,
    ILogger<ConfirmedAddressSwap> logger)
{
    public async Task<Result> MoveAsync(
        Guid userId, string newEmail, SwapPrecondition precondition, CancellationToken ct)
    {
        var swapped = await userAccountService.SwapConfirmedAddressAsync(userId, newEmail, precondition, ct);
        if (swapped.IsFailure)
            return Result.Failure(swapped.Error);

        // Old-address security notice (CTO-bind #4): "your email was changed", so the previous owner can detect an
        // unauthorized change (OWASP ASVS V2.5 / NIST SP 800-63B). Best-effort, log-and-continue — a send failure
        // must never fail a completed change. No link, and it does not reveal the new address.
        if (swapped.Value.PreviousEmail is { Length: > 0 } previousEmail)
        {
            try
            {
                await emailSender.SendEmailChangedNotificationAsync(previousEmail, ct);
            }
            catch (Exception ex)
            {
                // §5 parity with the sender boundary: log only the exception TYPE and the opaque user id.
                LogOldAddressNotificationFailed(ex.GetType().Name, userId);
            }
        }

        return Result.Success();
    }

    [LoggerMessage(4002, LogLevel.Warning,
        "Address swap: old-address notification failed for user {UserId} ({ErrorType}) " +
        "(change succeeded)")]
    private partial void LogOldAddressNotificationFailed(string errorType, Guid userId);
}
