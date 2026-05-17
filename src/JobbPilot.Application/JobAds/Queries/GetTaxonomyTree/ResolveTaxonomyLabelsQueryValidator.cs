using FluentValidation;
using JobbPilot.Domain.SavedSearches;

namespace JobbPilot.Application.JobAds.Queries.GetTaxonomyTree;

/// <summary>
/// ADR 0043 MAP-3 — reverse-lookup-cap enforce:as i Validation-pipeline FÖRE
/// handlern. Cap = <see cref="SearchCriteria.MaxConceptIds"/> ×2 (en sparad
/// sökning bär som mest MaxConceptIds Ssyk + MaxConceptIds Region) — refererad
/// domänkonstant, ej hårdkodad (DRY; en sparad sökning kan aldrig be om fler
/// än domänen tillåter). Speglar <c>SuggestJobAdTermsQueryValidator</c>.
/// </summary>
public sealed class ResolveTaxonomyLabelsQueryValidator
    : AbstractValidator<ResolveTaxonomyLabelsQuery>
{
    // Ssyk + Region är två separata MaxConceptIds-listor i en sparad sökning.
    public static readonly int MaxConceptIdsPerCall = SearchCriteria.MaxConceptIds * 2;

    public ResolveTaxonomyLabelsQueryValidator()
    {
        // Cascade.Stop: utan den kör .Must() även när .NotNull() fallit →
        // NullReferenceException i stället för rent 400 (test-writer DEFEKT #2).
        RuleFor(q => q.ConceptIds)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .Must(ids => ids.Count <= MaxConceptIdsPerCall)
            .WithMessage($"Max {MaxConceptIdsPerCall} koder per anrop.");

        RuleForEach(q => q.ConceptIds)
            .NotEmpty()
            .MaximumLength(32)   // speglar SearchCriteria concept-id-format
            .WithMessage("Concept-id måste vara 1-32 tecken.");
    }
}
