using FluentValidation;
using Jobbliggaren.Application.JobAds.Queries.ListJobAds;
using Jobbliggaren.Application.RecentJobSearches.Common;

namespace Jobbliggaren.Application.RecentJobSearches.Commands.CaptureRecentSearch;

public sealed class CaptureRecentSearchCommandValidator : AbstractValidator<CaptureRecentSearchCommand>
{
    public CaptureRecentSearchCommandValidator()
    {
        // Before anything reads SortBy: ToDomainSort throws on a value outside the enum, which would be a 500.
        RuleFor(c => c.Sort).IsInEnum();

        Include(new RecentSearchCandidateValidator());

        // ADR 0042 Beslut D, as on the list.
        RuleFor(c => c.Q)
            .NotEmpty()
            .When(c => c.Sort == ListJobAdsSort.Relevance)
            .WithMessage("Relevans-sortering kräver att du anger en söktext.");
    }
}
