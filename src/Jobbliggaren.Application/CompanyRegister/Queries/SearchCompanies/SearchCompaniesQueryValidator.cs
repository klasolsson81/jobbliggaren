using FluentValidation;
using Jobbliggaren.Application.CompanyRegister.Abstractions;

namespace Jobbliggaren.Application.CompanyRegister.Queries.SearchCompanies;

/// <summary>
/// The pipeline TRANSPORT for the single normalizer — not a second rule-set (house rule: a rule
/// with two normalizers is two rules). <c>PagedResultContractTests</c> forbids
/// <c>Result&lt;PagedResult&lt;T&gt;&gt;</c>, so the normalizer's Validation errors must reach
/// the client through <c>ValidationBehavior</c>: this validator runs
/// <see cref="CompanyRegisterSearchCriteria.Create"/> and forwards its <c>DomainError</c>
/// verbatim (the Code as the failure key, the Swedish message as the text). The handler re-runs
/// the SAME Create and treats failure as unreachable — one authority, two call sites, zero
/// duplicated rules.
/// </summary>
public sealed class SearchCompaniesQueryValidator : AbstractValidator<SearchCompaniesQuery>
{
    public SearchCompaniesQueryValidator()
    {
        RuleFor(q => q).Custom((q, ctx) =>
        {
            var criteria = CompanyRegisterSearchCriteria.Create(
                q.SniCodes, q.MunicipalityCodes, q.Name, q.OrganizationNumber, q.Page, q.PageSize);

            if (criteria.IsFailure)
                ctx.AddFailure(criteria.Error.Code, criteria.Error.Message);
        });
    }
}
