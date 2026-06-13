using FluentValidation;

namespace Jobbliggaren.Application.SavedSearches.Queries.RunSavedSearch;

public sealed class RunSavedSearchQueryValidator
    : AbstractValidator<RunSavedSearchQuery>
{
    public RunSavedSearchQueryValidator()
    {
        RuleFor(q => q.Id).NotEmpty();
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);
    }
}
