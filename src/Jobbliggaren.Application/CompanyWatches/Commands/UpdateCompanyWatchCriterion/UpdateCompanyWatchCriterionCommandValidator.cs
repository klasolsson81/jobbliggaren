using FluentValidation;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Domain.CompanyWatches;

namespace Jobbliggaren.Application.CompanyWatches.Commands.UpdateCompanyWatchCriterion;

/// <summary>
/// PATCH semantics: members are optional, but a PRESENT <c>Criteria</c> runs the full shared
/// predicate validation (C-D12 raw caps + existence — same rules as create; a PATCH is not a
/// backdoor past them).
/// </summary>
public sealed class UpdateCompanyWatchCriterionCommandValidator
    : AbstractValidator<UpdateCompanyWatchCriterionCommand>
{
    public UpdateCompanyWatchCriterionCommandValidator(ICriterionReferenceProvider reference)
    {
        RuleFor(c => c.Id).NotEmpty();

        RuleFor(c => c.Criteria!)
            .SetValidator(new CompanyWatchCriteriaInputValidator(reference))
            .When(c => c.Criteria is not null);

        RuleFor(c => c.Label)
            .MaximumLength(CompanyWatchCriterion.LabelMaxLength)
                .WithMessage($"Namn får vara max {CompanyWatchCriterion.LabelMaxLength} tecken.");
    }
}
