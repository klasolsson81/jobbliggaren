using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Auth.AccountEmailChanges;
using Jobbliggaren.Application.Common.Abstractions;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Infrastructure.Auth.Access;

public sealed partial class AccountAccessCleanup(
    ISessionStore sessions, IAccountEmailChangeStore addressChanges, ILogger<AccountAccessCleanup> logger)
    : IAccountAccessCleanup
{
    public async Task CompleteAsync(AccountAccessChanged change, CancellationToken cancellationToken)
    {
        try
        {
            if (change.PendingDeletion)
                await sessions.MarkUserDeletedAsync(change.UserId, cancellationToken);
            await sessions.InvalidateBeforeRevisionAsync(change.UserId, change.AccessRevision, cancellationToken);
        }
        catch (StoreUnavailableException exception)
        {
            LogCleanupFailed(logger, change.UserId, "Sessions", exception.GetType().Name);
        }
        try
        {
            await addressChanges.CancelBeforeRevisionAsync(change.UserId, change.AccessRevision, cancellationToken);
        }
        catch (StoreUnavailableException exception)
        {
            LogCleanupFailed(logger, change.UserId, "AddressChange", exception.GetType().Name);
        }
    }

    [LoggerMessage(1030, LogLevel.Warning,
        "Committed account access change: cleanup of {Resource} failed for {UserId} ({ErrorType}); older generations remain inadmissible")]
    private static partial void LogCleanupFailed(ILogger logger, Guid userId, string resource, string errorType);
}
