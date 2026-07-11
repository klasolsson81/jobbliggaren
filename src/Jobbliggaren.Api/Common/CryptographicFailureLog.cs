using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Api.Common;

/// <summary>
/// Fas 4b PR-9b (security-auditor Minor 1) — high-performance (CA1848) source-generated log for a
/// <see cref="System.Security.Cryptography.CryptographicException"/> that surfaced to the top-level
/// Api exception middleware (e.g. the Form C read-path opener failing closed on a cold/missing DEK
/// or a tampered/wrong-key ciphertext). Logs the exception TYPE ONLY — never the message, never the
/// exception, never any PII/DEK bytes — so a ciphertext-tampering / crypto anomaly keeps an
/// integrity signal even for a failure that never entered the Mediator pipeline (where
/// <c>LoggingBehavior</c> would already have logged it), while the client body stays a bare,
/// detail-free 500.
/// </summary>
internal static partial class CryptographicFailureLog
{
    [LoggerMessage(EventId = 4200, Level = LogLevel.Warning,
        Message = "cryptographic_failure type={CryptographicExceptionType}")]
    public static partial void CryptographicFailure(ILogger logger, string cryptographicExceptionType);
}
