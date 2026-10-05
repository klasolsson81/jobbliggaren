using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Api.Observability;

/// <summary>
/// #1975 (ADR 0153) — the public completion of an administrator-initiated address change could not invalidate the
/// account's sessions after the address had moved. Error, carrying the account's id only: the route is anonymous, so no
/// logging scope names a user, and an operator has to log the account out by hand.
/// </summary>
internal static partial class AccountEmailChangeLog
{
    [LoggerMessage(EventId = 2060, Level = LogLevel.Error,
        Message = "Account email change: the sessions of user {TargetUserId} were not invalidated after the address "
            + "moved ({ErrorType})")]
    public static partial void TeardownFailed(ILogger logger, Guid targetUserId, string errorType);
}
