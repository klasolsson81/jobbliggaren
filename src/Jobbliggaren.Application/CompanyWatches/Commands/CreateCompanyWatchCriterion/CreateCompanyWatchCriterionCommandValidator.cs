using FluentValidation;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Domain.CompanyWatches;

namespace Jobbliggaren.Application.CompanyWatches.Commands.CreateCompanyWatchCriterion;

/// <summary>
/// Delegates the predicate to the shared <see cref="CompanyWatchCriteriaInputValidator"/> (C-D12
/// raw caps + per-axis existence — rule order documented there). The label gets its RAW length cap
/// here for the same C-D12 reason: the Domain's <c>NormalizeLabel</c> trims before it measures.
/// </summary>
public sealed class CreateCompanyWatchCriterionCommandValidator
    : AbstractValidator<CreateCompanyWatchCriterionCommand>
{
    public CreateCompanyWatchCriterionCommandValidator(ICriterionReferenceProvider reference)
    {
        RuleFor(c => c.Criteria)
            .NotNull()
                .WithMessage("Bevakningens kriterier krävs.")
            .SetValidator(new CompanyWatchCriteriaInputValidator(reference));

        RuleFor(c => c.Label)
            .MaximumLength(CompanyWatchCriterion.LabelMaxLength)
                .WithMessage($"Namn får vara max {CompanyWatchCriterion.LabelMaxLength} tecken.");
    }
}
