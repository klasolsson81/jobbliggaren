using FluentValidation;
using Jobbliggaren.Domain.SavedSearches;

namespace Jobbliggaren.Application.Matching.Queries.ResolveSkillLabels;

/// <summary>
/// DoS bound for the skill reverse-lookup (ADR 0079 STEG 3 PR-C): the id list is capped at
/// the single authoritative <see cref="SearchCriteria.MaxConceptIds"/> (= the
/// PreferredSkills cap, so a legitimate full saved set always resolves). An empty list is
/// valid (→ empty result). Mirrors the occupation reverse-lookup's cap discipline.
/// </summary>
public sealed class ResolveSkillLabelsQueryValidator : AbstractValidator<ResolveSkillLabelsQuery>
{
    public ResolveSkillLabelsQueryValidator()
    {
        RuleFor(q => q.ConceptIds)
            .NotNull()
            .Must(ids => ids.Count <= SearchCriteria.MaxConceptIds)
            .When(q => q.ConceptIds is not null)
            .WithMessage($"Max {SearchCriteria.MaxConceptIds} kompetens-id per uppslag.");
    }
}
