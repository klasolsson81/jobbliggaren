using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.CompanyWatches.Commands;

/// <summary>
/// ADR 0087 D3 (FORK B1) — the shared idempotent follow path for a <see cref="CompanyWatch"/>, single-
/// sourced so the two entry points that reach it cannot drift: <c>FollowCompanyCommand</c> (org.nr from
/// the request body) and <c>FollowCompanyFromJobAdCommand</c> (#455, org.nr resolved server-side from
/// the ad). Both hand a validated <see cref="OrganizationNumber"/> here; the resurrect + unique-race
/// mechanics live in exactly one place.
///
/// <para>
/// <b>Idempotency (unchanged from PR-3):</b> an existing (active or soft-deleted) row for
/// <c>(userId, org.nr)</c> is found via <c>IgnoreQueryFilters</c> and RESURRECTED (<c>Refollow</c>
/// no-ops if already active) — there is exactly one physical row per pair, ever. The concurrent
/// fresh-follow race is caught on the active-partial UNIQUE and returns the winner's id.
/// </para>
/// </summary>
internal static class CompanyWatchFollowExecutor
{
    public static async Task<Result<Guid>> FollowOrResurrectAsync(
        IAppDbContext db,
        IDbExceptionInspector dbExceptionInspector,
        Guid userId,
        OrganizationNumber organizationNumber,
        IDateTimeProvider clock,
        CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters — a previously unfollowed (soft-deleted) row must be FOUND so it can be
        // resurrected. Querying active-only would miss it and insert a second physical row (the active-
        // partial unique does not block a soft-deleted row). Tracked so a resurrect persists.
        var existing = await db.CompanyWatches
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                w => w.UserId == userId && w.OrganizationNumber == organizationNumber, cancellationToken);

        if (existing is not null)
        {
            existing.Refollow(clock);
            return Result.Success(existing.Id.Value);
        }

        var watchResult = CompanyWatch.Follow(userId, organizationNumber, clock);
        if (watchResult.IsFailure)
            return Result.Failure<Guid>(watchResult.Error);

        var watch = watchResult.Value;
        db.CompanyWatches.Add(watch);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (dbExceptionInspector.IsUniqueConstraintViolation(ex))
        {
            // Race: a concurrent fresh follow of the same (user, org.nr) won. Detach our failed insert
            // and return the winner's id (idempotent semantics, parity with SaveJobAd).
            db.Detach(watch);

            var winner = await db.CompanyWatches
                .AsNoTracking()
                .Where(w => w.UserId == userId && w.OrganizationNumber == organizationNumber)
                .Select(w => w.Id)
                .FirstAsync(cancellationToken);

            return Result.Success(winner.Value);
        }

        return Result.Success(watch.Id.Value);
    }
}
