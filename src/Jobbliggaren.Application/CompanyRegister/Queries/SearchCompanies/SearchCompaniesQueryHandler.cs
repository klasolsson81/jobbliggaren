using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Queries.BrowseCompanies;
using Mediator;

namespace Jobbliggaren.Application.CompanyRegister.Queries.SearchCompanies;

/// <summary>
/// Thin by design: <c>Create</c> (the single normalizer) → the port → the DTO mask. The register
/// is NOT reachable from here except through <see cref="ICompanyRegisterSearchQuery"/> (DPIA
/// C-D4/M-C5 firewall — the register is not a <c>DbSet</c> on <c>IAppDbContext</c>), and every
/// row is routed through <see cref="CompanyBrowseDto.FromRow"/> so a personnummer-shaped org.nr
/// can never surface raw (ADR 0087 D8(c) defense-in-depth).
///
/// <para>
/// <b>Counts-only logging (DPIA C-D5):</b> neither the org.nr nor the company name of any hit is
/// ever logged. Pinned mechanically: this file is listed in
/// <c>OrganizationNumberSurfacingGuardTests.RawOrgNrReadingSourcePaths</c>, whose log-boundary
/// scan fails the build on any log call in it that carries an org.nr token.
/// </para>
/// </summary>
public sealed class SearchCompaniesQueryHandler(ICompanyRegisterSearchQuery search)
    : IQueryHandler<SearchCompaniesQuery, PagedResult<CompanyBrowseDto>>
{
    public async ValueTask<PagedResult<CompanyBrowseDto>> Handle(
        SearchCompaniesQuery query, CancellationToken cancellationToken)
    {
        var criteria = CompanyRegisterSearchCriteria.Create(
            query.SniCodes, query.MunicipalityCodes, query.Name, query.OrganizationNumber,
            query.Page, query.PageSize);

        if (criteria.IsFailure)
        {
            // Unreachable by construction: SearchCompaniesQueryValidator runs the SAME Create in
            // the pipeline and 400s first. Reaching this line means the validator and the
            // normalizer have drifted apart — a bug, never user input: fail loud (500), don't
            // guess a response.
            throw new InvalidOperationException(
                "CompanyRegisterSearchCriteria.Create failed post-validation: "
                + criteria.Error.Code);
        }

        var page = await search.SearchAsync(criteria.Value, cancellationToken);

        var items = page.Items.Select(CompanyBrowseDto.FromRow).ToList();

        return new PagedResult<CompanyBrowseDto>(
            items, page.TotalCount, page.Page, page.PageSize);
    }
}
