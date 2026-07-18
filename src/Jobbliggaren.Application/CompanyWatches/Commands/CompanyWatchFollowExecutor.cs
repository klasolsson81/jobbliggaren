using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Security;
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
///
/// <para>
/// <b>#544 (ADR 0090 D5) — the personnummer-token seam.</b> This is the single write point where the
/// at-rest posture is decided (security-auditor B2). A personnummer-shaped (enskild-firma) org.nr
/// equals the owner's personnummer, so it is stored as a keyed HMAC token
/// (<see cref="IProtectedIdentityTokenizer"/>) rather than plaintext; a legal-entity (AB) org.nr is
/// public and stored verbatim. The discriminator is the single-sourced
/// <see cref="OrganizationNumber.IsPersonnummerShaped"/>, evaluated HERE on the raw value — the one
/// moment the plaintext exists before it is discarded. The lookup then dual-probes token-or-plaintext
/// during the backfill window so a legacy plaintext row is resurrected, never duplicated.
/// </para>
/// </summary>
internal static class CompanyWatchFollowExecutor
{
    public static async Task<Result<Guid>> FollowOrResurrectAsync(
        IAppDbContext db,
        IDbExceptionInspector dbExceptionInspector,
        IProtectedIdentityTokenizer tokenizer,
        Guid userId,
        OrganizationNumber organizationNumber,
        IDateTimeProvider clock,
        CancellationToken cancellationToken)
    {
        // #544: decide the stored key ONCE, here, on the raw value. A pnr-shaped org.nr is tokenised
        // (never stored plaintext); an AB org.nr is stored verbatim (storedKey == organizationNumber).
        var storedKey = organizationNumber.IsPersonnummerShaped()
            ? OrganizationNumber.FromTrusted(tokenizer.Tokenize(organizationNumber.Value))
            : organizationNumber;

        // IgnoreQueryFilters — a previously unfollowed (soft-deleted) row must be FOUND so it can be
        // resurrected. Querying active-only would miss it and insert a second physical row (the active-
        // partial unique does not block a soft-deleted row). Tracked so a resurrect persists.
        //
        // Dual-probe (transition-scoped): during the plaintext→token backfill window a legacy pnr row
        // may still hold the plaintext org.nr while new writes hold the token — match EITHER so a soft-
        // deleted legacy row is resurrected, not duplicated. For an AB org.nr the two operands are the
        // same instance, so this collapses to the plaintext probe. Retire the raw arm once the backfill
        // fitness proves zero plaintext pnr rows remain (harmless to retain: post-backfill the raw arm
        // matches nothing for a pnr firma).
        var existing = await db.CompanyWatches
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                w => w.UserId == userId
                    && (w.OrganizationNumber == storedKey || w.OrganizationNumber == organizationNumber),
                cancellationToken);

        if (existing is not null)
        {
            existing.Refollow(clock);
            return Result.Success(existing.Id.Value);
        }

        var watchResult = CompanyWatch.Follow(userId, storedKey, clock);
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
            // and return the winner's id (idempotent semantics, parity with SaveJobAd). Same dual-probe.
            db.Detach(watch);

            var winner = await db.CompanyWatches
                .AsNoTracking()
                .Where(w => w.UserId == userId
                    && (w.OrganizationNumber == storedKey || w.OrganizationNumber == organizationNumber))
                .Select(w => w.Id)
                .FirstAsync(cancellationToken);

            return Result.Success(winner.Value);
        }

        return Result.Success(watch.Id.Value);
    }
}
