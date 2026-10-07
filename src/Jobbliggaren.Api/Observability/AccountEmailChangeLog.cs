using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Api.Observability;

/// <summary>
/// Post-commit operational failures preserve the known account-transition outcome.
/// Only the opaque account id and exception type are recorded.
/// </summary>
internal static partial class AccountEmailChangeLog
{
    [LoggerMessage(EventId = 2060, Level = LogLevel.Error,
        Message = "Committed account transition: post-commit cleanup or replacement session issuance failed for "
            + "user {TargetUserId} ({ErrorType}); earlier credentials remain inadmissible")]
    public static partial void TeardownFailed(ILogger logger, Guid targetUserId, string errorType);
}
