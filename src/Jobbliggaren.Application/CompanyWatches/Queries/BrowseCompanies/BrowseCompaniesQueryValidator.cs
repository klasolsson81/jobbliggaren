using FluentValidation;
using Jobbliggaren.Application.CompanyWatches.Abstractions;

namespace Jobbliggaren.Application.CompanyWatches.Queries.BrowseCompanies;

/// <summary>
/// #560 PR-2 — transport bounds only. The PREDICATE needs no validation here: it is not user input on
/// this request, it is the persisted <c>CompanyWatchCriteriaSpec</c> the Domain already validated when
/// the criterion was created (PR-3's write path).
/// </summary>
public sealed class BrowseCompaniesQueryValidator : AbstractValidator<BrowseCompaniesQuery>
{
    public BrowseCompaniesQueryValidator()
    {
        RuleFor(q => q.CriterionId).NotEmpty();

        // Both axes bounded, from the SAME constants the port caps its count against — that is what
        // makes "TotalPages can never exceed MaxPage" true by construction rather than by coincidence.
        // See CompanyBrowseCriteria.MaxServableRows.
        RuleFor(q => q.Page).InclusiveBetween(1, CompanyBrowseCriteria.MaxPage);
        RuleFor(q => q.PageSize).InclusiveBetween(1, CompanyBrowseCriteria.MaxPageSize);
    }
}
