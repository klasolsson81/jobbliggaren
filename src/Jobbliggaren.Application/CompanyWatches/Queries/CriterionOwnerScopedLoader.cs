using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.CompanyWatches;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.CompanyWatches.Queries;

/// <summary>
/// The owner-scoped criterion load with the ADR 0031 cross-user probe, single-sourced (#1559).
///
/// <para>
/// <b>Why this is extracted rather than copied a fourth time.</b> Four read handlers now run the same
/// three-step procedure — load for THIS user, and on a miss re-probe without the owner predicate to
/// tell an unknown id apart from a cross-user probe WITHOUT telling the caller apart. That is an IDOR
/// posture, not a convenience: a copy that drops the second step still returns the right value and
/// passes every semantic test while silently discarding the probing signal. CLAUDE.md §3.6's
/// rule-of-three is met, and the repo's own "two copies, not three" precedent
/// (<c>ListCompanyWatchesQueryHandler.PnrShapedAdPredicate</c>) was justified by an OBSTACLE — LinqKit
/// being off the BUILD.md §3.1 allowlist. There is no obstacle here.
/// </para>
///
/// <para>
/// <b>Returns null for both misses, deliberately</b> — literally the same value, so no response can be
/// used as an existence oracle for another user's criterion ids (the endpoints map null → 404, never
/// 403). Fail-closed on no authenticated user: never a <c>Guid.Empty</c> fallback, which would scope
/// the read to a "user" every unauthenticated caller shares.
/// </para>
///
/// <para>
/// <b>Counts-only logging (DPIA C-D5).</b> The probe logs the criterion id and the user id — never an
/// org.nr and never a company name; nothing from the register passes through here at all.
/// </para>
/// </summary>
internal static class CriterionOwnerScopedLoader
{
    /// <param name="operation">
    /// The name recorded on a cross-user attempt. Each call site passes its OWN query name so the
    /// audit trail says which surface was probed — a shared constant would erase exactly that.
    /// </param>
    public static async Task<CompanyWatchCriterion?> LoadForCurrentUserAsync(
        IAppDbContext db,
        ICurrentUser currentUser,
        IFailedAccessLogger failedAccessLogger,
        Guid criterionId,
        string operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(currentUser);
        ArgumentNullException.ThrowIfNull(failedAccessLogger);

        if (!currentUser.UserId.HasValue)
            return null;

        var userId = currentUser.UserId.Value;
        var id = new CompanyWatchCriterionId(criterionId);

        // Hard delete (G1/C-D8) removes the row outright — a missing criterion is genuinely absent,
        // not soft-hidden, so no query filter hides rows from this read.
        var criterion = await db.CompanyWatchCriteria
            .AsNoTracking()
            .Where(c => c.Id == id && c.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken);

        if (criterion is not null)
            return criterion;

        // The house fetch-then-check pattern: a single `Id == id && UserId == userId` predicate would
        // return the same null while silently throwing the probing signal away.
        var existsForSomebodyElse = await db.CompanyWatchCriteria
            .AsNoTracking()
            .AnyAsync(c => c.Id == id, cancellationToken);

        if (existsForSomebodyElse)
            failedAccessLogger.LogCrossUserAttempt("CompanyWatchCriterion", criterionId, userId, operation);

        return null;
    }
}
