using FluentValidation;
using Jobbliggaren.Application.CompanyRegister.Abstractions;

namespace Jobbliggaren.Application.CompanyRegister.Queries.GetCompanySearchMagnitude;

/// <summary>
/// The pipeline TRANSPORT for the single normalizer — see
/// <c>SearchCompaniesQueryValidator</c> for the full argument (one authority, two call sites,
/// zero duplicated rules). Paging is fixed (1/1): a magnitude has no page, and the fixed values
/// can never trip the caps.
/// </summary>
public sealed class GetCompanySearchMagnitudeQueryValidator
    : AbstractValidator<GetCompanySearchMagnitudeQuery>
{
    public GetCompanySearchMagnitudeQueryValidator()
    {
        RuleFor(q => q).Custom((q, ctx) =>
        {
            var criteria = CompanyRegisterSearchCriteria.Create(
                q.SniCodes, q.MunicipalityCodes, q.Name, q.OrganizationNumber,
                page: 1, pageSize: 1);

            if (criteria.IsFailure)
                ctx.AddFailure(criteria.Error.Code, criteria.Error.Message);
        });
    }
}
