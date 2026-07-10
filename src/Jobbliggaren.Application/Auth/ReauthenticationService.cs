using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Auth;

/// <summary>
/// Lifts the credential-check body out of the original <c>VerifyCredentialsQueryHandler</c> so the
/// re-auth policy lives in ONE place (both <c>ReauthenticationBehavior</c> and <c>/auth/verify</c>
/// consume it), and adds the Layer 1 soft-delete liveness gate.
/// </summary>
public sealed class ReauthenticationService(
    ICurrentUser currentUser,
    IUserAccountService userAccountService,
    IAppDbContext db,
    ISessionStore sessionStore)
    : IReauthenticationService
{
    public async ValueTask<Result> VerifyCurrentUserPasswordAsync(string? password, CancellationToken ct)
    {
        // Defense: the calling endpoint requires authorization, so ICurrentUser is set. Failsafe
        // so a misconfiguration cannot expose a sensitive op to an anonymous caller.
        if (!currentUser.UserId.HasValue)
            return InvalidCredentials();

        var userId = currentUser.UserId.Value;

        // Self-defending: callers validate NotEmpty (DeleteAccountCommandValidator /
        // VerifyCredentialsQueryValidator) before this runs, but the single re-auth source must not
        // depend on that — an empty password is never valid. Removes the bare null-forgiving below.
        if (string.IsNullOrEmpty(password))
            return InvalidCredentials();

        // SessionAuthenticationHandler sets no email claim — resolve from Identity by userId so we
        // do not depend on claim shape.
        var email = await userAccountService.GetEmailAsync(userId, ct);
        if (string.IsNullOrEmpty(email))
            return InvalidCredentials();

        // Lockout-aware (CTO condition 2 / #503, OWASP ASVS re-auth + anti-automation): re-auth
        // shares login's brute-force protection, so it can never be an unlocked bypass. A locked
        // account yields Auth.AccountLocked internally, normalized to Auth.InvalidCredentials on
        // the wire. Validated FIRST (before the soft-delete gate) so a wrong-password attempt takes
        // the identical path regardless of account state — no timing/response oracle on soft-delete
        // status (M8 discipline).
        var credentialsResult = await userAccountService.ValidateCredentialsAsync(email, password, ct);
        if (credentialsResult.IsFailure)
            // #714 (CTO-bind Risk 2): the shared ValidateCredentialsAsync may now emit
            // EmailNotConfirmed (the email-confirmation-first login gate). On the re-auth surface that
            // must stay a uniform 401 — normalize it back to InvalidCredentials so the distinct 403 arm
            // is reachable ONLY via LoginCommandHandler. Unreachable in practice (only confirmed users
            // hold sessions, and re-auth requires a session), but defense-in-depth keeps /auth/verify
            // and ReauthenticationBehavior byte-identical for every failure.
            return credentialsResult.Error.Code == AuthErrorCodes.EmailNotConfirmed
                ? InvalidCredentials()
                : Result.Failure(credentialsResult.Error);

        // TOCTOU defense: the email must resolve back to the same userId as the session.
        if (credentialsResult.Value.UserId != userId)
            return InvalidCredentials();

        // Layer 1 soft-delete liveness gate: a soft-deleted-but-not-hard-deleted account (30d
        // window) whose session outlived deletion must not run a sensitive op — reject it, and
        // best-effort self-heal by tearing down its surviving sessions (complements PR2c-0's
        // Layer 2 :deleted tombstone, which fail-closes the read path; this covers the rare case
        // where the tombstone was never planted, e.g. Redis was down at deletion). IgnoreQueryFilters
        // — the global DeletedAt==null filter would hide the row; keyed userId -> JobSeeker.UserId
        // (as LoginCommandHandler). Runs AFTER the password check so a wrong password never reveals
        // soft-delete state.
        var deletedAt = await db.JobSeekers
            .IgnoreQueryFilters()
            .Where(js => js.UserId == userId)
            .Select(js => (DateTimeOffset?)js.DeletedAt)
            .FirstOrDefaultAsync(ct);

        if (deletedAt is not null)
        {
            try
            {
                await sessionStore.InvalidateAllForUserAsync(userId, ct);
            }
            catch
            {
                // Best-effort self-heal — the reject below is the security-relevant outcome, and
                // Layer 2's tombstone already fail-closes the read path. A Redis failure here must
                // not turn the gate into a 500 that leaks "soft-deleted" via a distinct status.
            }

            return InvalidCredentials();
        }

        return Result.Success();
    }

    private static Result InvalidCredentials() =>
        Result.Failure(
            DomainError.Validation(AuthErrorCodes.InvalidCredentials, AuthErrorCodes.InvalidCredentialsMessage));
}
