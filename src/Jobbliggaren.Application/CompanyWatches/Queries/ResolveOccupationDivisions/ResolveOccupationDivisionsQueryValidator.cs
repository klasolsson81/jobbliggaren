using FluentValidation;

namespace Jobbliggaren.Application.CompanyWatches.Queries.ResolveOccupationDivisions;

public sealed class ResolveOccupationDivisionsQueryValidator
    : AbstractValidator<ResolveOccupationDivisionsQuery>
{
    public ResolveOccupationDivisionsQueryValidator()
    {
        RuleFor(q => q.Word)
            .Must(w => w is not null && w.Trim().Length >= ResolveOccupationDivisionsQuery.MinWordLength)
                .WithMessage($"Sökordet måste vara minst {ResolveOccupationDivisionsQuery.MinWordLength} tecken.")
            .Must(w => w is null || w.Trim().Length <= ResolveOccupationDivisionsQuery.MaxWordLength)
                .WithMessage($"Sökordet får vara högst {ResolveOccupationDivisionsQuery.MaxWordLength} tecken.");
    }
}
