using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Application.Common.Abstractions;

/// <summary>
/// Single source of the re-authentication check for sensitive operations (C5, epik #481).
/// Resolves the acting user from <see cref="ICurrentUser"/>, verifies the supplied password via
/// the lockout-aware <see cref="IUserAccountService.ValidateCredentialsAsync"/> (so re-auth shares
/// login's anti-automation and cannot become an unlocked brute-force bypass), and gates a
/// soft-deleted account (Layer 1 — reject + best-effort session self-heal). Returns a bare
/// <see cref="Result"/>: success, or <c>Auth.InvalidCredentials</c> for ANY failure (wrong
/// password, locked, or soft-deleted — indistinguishable, oracle-avoidance). No session mutation
/// on the success path.
///
/// Consumed by <c>ReauthenticationBehavior</c> (throws on failure) and by the
/// <c>VerifyCredentialsQuery</c> handler backing <c>POST /auth/verify</c> (returns the Result) —
/// one enforcement path for the whole app.
/// </summary>
public interface IReauthenticationService
{
    ValueTask<Result> VerifyCurrentUserPasswordAsync(string? password, CancellationToken ct);
}
