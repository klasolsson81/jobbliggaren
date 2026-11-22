using FluentValidation;
using Jobbliggaren.Domain.SavedSearches;

namespace Jobbliggaren.Application.JobSeekers.Commands.AddPreferredSkills;

/// <summary>
/// #2247 — the same concept-id rule <c>UpdateMatchPreferencesCommandValidator</c> applies to the skills part,
/// and at least one id: an empty add has nothing to do and is a client defect, not a no-op.
/// </summary>
public sealed class AddPreferredSkillsCommandValidator : AbstractValidator<AddPreferredSkillsCommand>
{
    private const string ConceptIdPattern = @"^[A-Za-z0-9_-]{1,32}\z";

    public AddPreferredSkillsCommandValidator()
    {
        RuleFor(c => c.ConceptIds)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithMessage("Listan med kompetenser måste anges.")
            .NotEmpty()
            .WithMessage("Minst en kompetens måste anges.")
            .Must(l => l.Count <= SearchCriteria.MaxConceptIds)
            .WithMessage($"Max {SearchCriteria.MaxConceptIds} kompetenser.")
            .ForEach(item => item.Matches(ConceptIdPattern)
                .WithMessage("Kompetens måste vara en giltig JobTech skill-concept-id (1-32 tecken, alfanumeriskt + _-)."));
    }
}
