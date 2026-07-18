using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.CompanyRegister.Queries.GetCompanySearchMagnitude;

/// <summary>
/// <c>Create</c> (the single normalizer) → the port's magnitude count over the SAME predicate
/// authority as the page query (one predicate, two ceilings — the drift defense the sibling
/// criterion port bound in Fork G3). Register behind the port only (DPIA C-D4).
/// </summary>
public sealed class GetCompanySearchMagnitudeQueryHandler(ICompanyRegisterSearchQuery search)
    : IQueryHandler<GetCompanySearchMagnitudeQuery, CompanySearchMagnitudeDto>
{
    public async ValueTask<CompanySearchMagnitudeDto> Handle(
        GetCompanySearchMagnitudeQuery query, CancellationToken cancellationToken)
    {
        // Paging is irrelevant to a magnitude; the VO still wants legal values (its caps guard
        // the OFFSET surface the magnitude query never uses). Fixed 1/1 — never user input.
        var criteria = CompanyRegisterSearchCriteria.Create(
            query.SniCodes, query.MunicipalityCodes, query.Name, query.OrganizationNumber,
            page: 1, pageSize: 1);

        if (criteria.IsFailure)
        {
            // Unreachable by construction (the validator runs the SAME Create) — see
            // SearchCompaniesQueryHandler for the drift argument.
            throw new InvalidOperationException(
                "CompanyRegisterSearchCriteria.Create failed post-validation: "
                + criteria.Error.Code);
        }

        var magnitude = await search.CountMatchingAsync(
            criteria.Value, CompanySearchMagnitudeDto.Ceiling, cancellationToken);

        return new CompanySearchMagnitudeDto(
            magnitude, Saturated: magnitude >= CompanySearchMagnitudeDto.Ceiling);
    }
}
