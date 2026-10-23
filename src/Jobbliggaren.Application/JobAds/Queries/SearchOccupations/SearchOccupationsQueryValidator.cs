using FluentValidation;

namespace Jobbliggaren.Application.JobAds.Queries.SearchOccupations;

/// <summary>Measures the trimmed query; the messages name only the bounds, never the query.</summary>
public sealed class SearchOccupationsQueryValidator : AbstractValidator<SearchOccupationsQuery>
{
    public SearchOccupationsQueryValidator()
    {
        RuleFor(q => q.Query)
            .Must(q => q is not null && q.Trim().Length >= SearchOccupationsQuery.MinQueryLength)
                .WithMessage($"Sökordet måste vara minst {SearchOccupationsQuery.MinQueryLength} tecken.")
            .Must(q => q is null || q.Trim().Length <= SearchOccupationsQuery.MaxQueryLength)
                .WithMessage($"Sökordet får vara högst {SearchOccupationsQuery.MaxQueryLength} tecken.");
    }
}
