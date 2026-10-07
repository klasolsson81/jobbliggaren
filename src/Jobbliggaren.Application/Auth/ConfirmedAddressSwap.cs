using Jobbliggaren.Application.Auth.Access;
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
    ILogger<ConfirmedAddressSwap> logger,
    IAccountAccessWriter accessWriter)
{
    private readonly List<(Guid UserId, string Email)> _pendingNotices = [];

    public async Task<Result<AccountAccessSnapshot>> MoveAsync(
        Guid userId, string newEmail, SwapPrecondition precondition, CancellationToken ct)
    {
        var swapped = await userAccountService.SwapConfirmedAddressAsync(userId, newEmail, precondition, ct);
        if (swapped.IsFailure)
            return Result.Failure<AccountAccessSnapshot>(swapped.Error);

        var transition = await accessWriter.AdvanceCredentialsAsync(userId, ct);

        if (swapped.Value.PreviousEmail is { Length: > 0 } previousEmail)
            _pendingNotices.Add((userId, previousEmail));

        return Result.Success(transition);
    }

    public void DiscardNotices() => _pendingNotices.Clear();

    public async Task NotifyCommittedAsync(CancellationToken ct)
    {
        var notices = _pendingNotices.ToArray();
        _pendingNotices.Clear();
        foreach (var (userId, previousEmail) in notices)
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

    }

    [LoggerMessage(4002, LogLevel.Warning,
        "Address swap: old-address notification failed for user {UserId} ({ErrorType}) " +
        "(change succeeded)")]
    private partial void LogOldAddressNotificationFailed(string errorType, Guid userId);
}
