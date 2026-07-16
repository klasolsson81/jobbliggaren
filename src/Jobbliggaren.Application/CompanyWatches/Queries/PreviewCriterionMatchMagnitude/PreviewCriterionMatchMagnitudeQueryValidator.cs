using FluentValidation;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Commands;

namespace Jobbliggaren.Application.CompanyWatches.Queries.PreviewCriterionMatchMagnitude;

/// <summary>
/// The preview runs the SAME shared predicate validation as create/update (C-D12 raw caps +
/// existence) — a debounced picker preview is the most request-frequent surface this input has,
/// which makes it the LAST place the raw length cap may be skipped.
/// </summary>
public sealed class PreviewCriterionMatchMagnitudeQueryValidator
    : AbstractValidator<PreviewCriterionMatchMagnitudeQuery>
{
    public PreviewCriterionMatchMagnitudeQueryValidator(ICriterionReferenceProvider reference)
    {
        RuleFor(q => q.Criteria)
            .NotNull()
                .WithMessage("Bevakningens kriterier krävs.")
            .SetValidator(new CompanyWatchCriteriaInputValidator(reference));
    }
}
