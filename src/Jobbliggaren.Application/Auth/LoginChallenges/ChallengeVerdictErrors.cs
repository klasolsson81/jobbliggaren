using System.Diagnostics;
using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Application.Auth.LoginChallenges;

/// <summary>
/// The one mapping from a refused code to its error, shared by the login arm and the re-authentication arm
/// (#1739): the same code pair for both, since the page copy is one form. Keyed on the attempt count, so it
/// stays true if <see cref="LoginChallengePolicy.MaxAttempts"/> moves.
/// </summary>
public static class ChallengeVerdictErrors
{
    /// <summary>The error a non-verified verdict maps to. A verified verdict has none and is refused here.</summary>
    public static DomainError For(ChallengeVerdict verdict) => verdict.Outcome switch
    {
        // The page warns before the burn.
        ChallengeOutcome.Wrong when verdict.AttemptsRemaining == 1 => DomainError.Validation(
            AuthErrorCodes.LoginCodeWrongLastAttempt, AuthErrorCodes.LoginCodeWrongLastAttemptMessage),
        ChallengeOutcome.Wrong => DomainError.Validation(
            AuthErrorCodes.LoginCodeWrong, AuthErrorCodes.LoginCodeWrongMessage),
        ChallengeOutcome.Burned => DomainError.Gone(
            AuthErrorCodes.LoginCodeBurned, AuthErrorCodes.LoginCodeBurnedMessage),
        ChallengeOutcome.Missing => DomainError.Gone(
            AuthErrorCodes.LoginCodeExpired, AuthErrorCodes.LoginCodeExpiredMessage),
        ChallengeOutcome.Verified => throw new ArgumentException("A verified verdict is not an error.", nameof(verdict)),
        var other => throw new UnreachableException($"Unmapped challenge outcome {other}."),
    };
}
