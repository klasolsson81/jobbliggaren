using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Domain.CompanyWatches;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.CompanyWatches.Queries.BrowseCompanies;

/// <summary>
/// #560 kriterie-vågen PR-2 — loads the user's criterion (owner-scoped) and RUNS it as a browse over
/// the local SCB company register via <see cref="ICompanyWatchBrowseQuery"/>. Structurally the sibling
/// of <c>RunSavedSearchQueryHandler</c>: load a user-owned PREDICATE by id, then execute it through a
/// port.
///
/// <para>
/// <b>The register is NOT reachable from here</b> — it is not a <c>DbSet</c> on
/// <see cref="IAppDbContext"/> (DPIA C-D4 / M-C5, enforced by a fail-closed architecture test). This
/// handler can read the user's criterion and nothing else; the register only answers through the port.
/// That firewall is what makes it structurally impossible for a handler to join the register against
/// personnummer-lookup output.
/// </para>
///
/// <para>
/// <b>IDOR posture (ADR 0031).</b> "Criterion does not exist" and "criterion belongs to somebody else"
/// both return <c>null</c> — literally the same value, so the response can never be used as an
/// existence oracle for another user's criterion ids (the endpoint maps null → 404, never 403). A
/// cross-user attempt is still DETECTED: the miss is re-probed without the owner predicate and, if the
/// id exists, recorded via <see cref="IFailedAccessLogger.LogCrossUserAttempt"/> (the house
/// fetch-then-check pattern; a single <c>Id == id &amp;&amp; UserId == userId</c> predicate would
/// silently throw that probing signal away).
/// </para>
///
/// <para>
/// <b>Counts-only logging (DPIA C-D5).</b> Neither the org.nr nor the company name of any browse hit
/// is ever logged. Pinned mechanically: this file is listed in
/// <c>OrganizationNumberSurfacingGuardTests.RawOrgNrReadingSourcePaths</c>, whose log-boundary scan
/// fails the build on any log call in it that carries an org.nr token.
/// </para>
/// </summary>
public sealed class BrowseCompaniesQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IFailedAccessLogger failedAccessLogger,
    ICompanyWatchBrowseQuery browse)
    : IQueryHandler<BrowseCompaniesQuery, PagedResult<CompanyBrowseDto>?>
{
    public async ValueTask<PagedResult<CompanyBrowseDto>?> Handle(
        BrowseCompaniesQuery query, CancellationToken cancellationToken)
    {
        // Fail-closed: no authenticated user → not-found. Never fall back to Guid.Empty, which would
        // scope the read to a "user" that any unauthenticated caller shares.
        if (!currentUser.UserId.HasValue)
            return null;

        var userId = currentUser.UserId.Value;
        var criterionId = new CompanyWatchCriterionId(query.CriterionId);

        // Hard delete (G1/C-D8) removes the row outright — a missing criterion is genuinely absent,
        // not soft-hidden. Nothing hides rows from this read: the vestigial DeletedAt query filter
        // that once had to be argued away here no longer exists (demolished with the column).
        var criterion = await db.CompanyWatchCriteria
            .AsNoTracking()
            .Where(c => c.Id == criterionId && c.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken);

        if (criterion is null)
        {
            // Failed-access detection (ADR 0031): tell an unknown id apart from a cross-user probe —
            // WITHOUT telling the caller apart. The response below is identical either way.
            var existsForSomebodyElse = await db.CompanyWatchCriteria
                .AsNoTracking()
                .AnyAsync(c => c.Id == criterionId, cancellationToken);

            if (existsForSomebodyElse)
            {
                failedAccessLogger.LogCrossUserAttempt(
                    "CompanyWatchCriterion", query.CriterionId, userId, "BrowseCompanies");
            }

            return null;
        }

        // criterion.Criteria is the EF-ignored computed VO over the aggregate's two text[] backing
        // fields. The port turns its two arrays into the `sni_codes && @sni` / `= ANY(@kommun)` SQL
        // parameters — it never sees the criterion aggregate itself.
        var page = await browse.BrowseAsync(
            new CompanyBrowseCriteria(criterion.Criteria, query.Page, query.PageSize),
            cancellationToken);

        var items = page.Items.Select(Mask).ToList();

        return new PagedResult<CompanyBrowseDto>(
            items, page.TotalCount, page.Page, page.PageSize);
    }

    /// <summary>
    /// Masks a personnummer-shaped org.nr at the Application boundary (ADR 0087 D8(c), §5). Normally
    /// unreachable — ADR 0091 keeps sole traders out of the register at ingest — but the masking is
    /// what makes a raw personnummer un-surfaceable by ANY future path, rather than by the continued
    /// correctness of a different subsystem's filter. Explicit mapping, never AutoMapper (§5).
    /// </summary>
    private static CompanyBrowseDto Mask(CompanyBrowseResult row)
    {
        var isProtected = OrganizationNumber.FromTrusted(row.OrganizationNumber).IsPersonnummerShaped();

        return new CompanyBrowseDto(
            OrganizationNumber: isProtected ? null : row.OrganizationNumber,
            IsProtectedIdentity: isProtected,
            Name: row.Name,
            SeatMunicipalityCode: row.SeatMunicipalityCode,
            SeatMunicipalityName: row.SeatMunicipalityName,
            SniCodes: row.SniCodes);
    }
}
