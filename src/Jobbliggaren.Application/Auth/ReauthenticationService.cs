using Jobbliggaren.Application.Auth.Grants;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Auth;

/// <summary>
/// The re-auth policy in ONE place (<c>ReauthenticationBehavior</c> consumes it): the grant is redeemed
/// against the session's user, then the Layer 1 soft-delete liveness gate runs. Since #1739 the credential
/// is a purpose-scoped grant (ADR 0142 D5), earned by the code mailed to the account's own address.
/// </summary>
public sealed class ReauthenticationService(
    ICurrentUser currentUser,
    IGrantStore grants,
    IAppDbContext db,
    ISessionStore sessionStore,
    IAuthAuditLogger audit)
    : IReauthenticationService
{
    public async ValueTask<Result> VerifyCurrentUserGrantAsync(string? grant, CancellationToken ct)
    {
        // Defense: the calling endpoint requires authorization, so ICurrentUser is set. Failsafe
        // so a misconfiguration cannot expose a sensitive op to an anonymous caller.
        if (!currentUser.UserId.HasValue)
            return InvalidCredentials();

        var userId = currentUser.UserId.Value;

        // Self-defending: the validators refuse an empty grant before this runs, but the single re-auth
        // source must not depend on that — an empty grant is never valid, and it reaches no store.
        if (string.IsNullOrEmpty(grant))
        {
            audit.ReauthenticationFailed(userId, GrantPurpose.Reauthentication);
            return InvalidCredentials();
        }

        // The store asserts the purpose AND the user (ADR 0142 D3): a grant issued to another user, for
        // another purpose, expired, unknown or already used is one answer. The redemption is single use
        // whichever way it ends, so a grant shown in the wrong session is dead for its owner too.
        var subject = await grants.RedeemAsync(
            GrantToken.FromRaw(grant),
            GrantAssertion.Of(new GrantSubject.Reauthentication(userId)),
            ct);
        if (subject is null)
        {
            audit.ReauthenticationFailed(userId, GrantPurpose.Reauthentication);
            return InvalidCredentials();
        }

        // Layer 1 soft-delete liveness gate: a soft-deleted-but-not-hard-deleted account (30d
        // window) whose session outlived deletion must not run a sensitive op — reject it, and
        // best-effort self-heal by tearing down its surviving sessions (complements PR2c-0's
        // Layer 2 :deleted tombstone, which fail-closes the read path; this covers the rare case
        // where the tombstone was never planted, e.g. Redis was down at deletion). IgnoreQueryFilters
        // — the global DeletedAt==null filter would hide the row; keyed userId -> JobSeeker.UserId.
        // #1349 — projected to a ROW, not to a nullable value, and that is the whole repair. The
        // previous `Select(js => (DateTimeOffset?)js.DeletedAt)` made FirstOrDefaultAsync answer null
        // for BOTH "no row at all" and "a live row", so this gate could not see an account with no
        // JobSeeker and let it through. An orphan holding a live session then passed re-auth, changed
        // its password, and was handed a FRESH session by the /change-password re-issue — renewing the
        // capability without ever crossing the login guard (security-auditor M-1).
        //
        // The predicate is deliberately the same rule as LoginSubjectResolver's: two gates, one sentence —
        // "a row with no JobSeeker is granted nothing". Read them together.
        var profile = await db.JobSeekers
            .IgnoreQueryFilters()
            .Where(js => js.UserId == userId)
            .Select(js => new { js.DeletedAt })
            .FirstOrDefaultAsync(ct);

        if (profile is null || profile.DeletedAt is not null)
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

            audit.ReauthenticationFailed(userId, GrantPurpose.Reauthentication);
            return InvalidCredentials();
        }

        audit.ReauthenticationSucceeded(userId, GrantPurpose.Reauthentication);
        return Result.Success();
    }

    private static Result InvalidCredentials() =>
        Result.Failure(
            DomainError.Validation(AuthErrorCodes.InvalidCredentials, AuthErrorCodes.InvalidCredentialsMessage));
}
