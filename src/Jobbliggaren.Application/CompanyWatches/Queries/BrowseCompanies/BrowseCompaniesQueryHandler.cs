using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Mediator;

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
/// cross-user attempt is still DETECTED — by <see cref="CriterionOwnerScopedLoader"/>, which owns the
/// posture for all four criterion read handlers and carries the reasoning.
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
        var criterion = await CriterionOwnerScopedLoader.LoadForCurrentUserAsync(
            db, currentUser, failedAccessLogger,
            query.CriterionId, CriterionReadOperation.BrowseCompanies, cancellationToken);

        if (criterion is null)
            return null;

        // criterion.Criteria is the EF-ignored computed VO over the aggregate's two text[] backing
        // fields. The port turns its two arrays into the `sni_codes && @sni` / `= ANY(@kommun)` SQL
        // parameters — it never sees the criterion aggregate itself.
        var page = await browse.BrowseAsync(
            new CompanyBrowseCriteria(criterion.Criteria, query.Page, query.PageSize),
            cancellationToken);

        // Masking is single-sourced on the DTO (CompanyBrowseDto.FromRow) since the company-search
        // wave added a second consumer of the same rule (ADR 0087 D8(c) is ONE knowledge piece).
        var items = page.Items.Select(CompanyBrowseDto.FromRow).ToList();

        return new PagedResult<CompanyBrowseDto>(
            items, page.TotalCount, page.Page, page.PageSize);
    }
}
