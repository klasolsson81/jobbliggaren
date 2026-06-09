using FluentValidation;
using JobbPilot.Domain.SavedSearches;

namespace JobbPilot.Application.JobAds.Queries.GetTaxonomyTree;

/// <summary>
/// ADR 0043 MAP-3 — reverse-lookup-cap enforce:as i Validation-pipeline FÖRE
/// handlern. Cap = <see cref="SearchCriteria.MaxConceptIds"/> ×4 — refererad
/// domänkonstant, ej hårdkodad (DRY; en chip-render kan aldrig be om fler koder
/// än de filterbara dimensionerna tillsammans tillåter). Speglar
/// <c>SuggestJobAdTermsQueryValidator</c>.
/// <para>
/// ADR 0043 implementerings-notat 2026-06-09 (ADR 0067 Platsbanken sök-paritet
/// Fas C1): multiplikator ×2→×4. Reverse-lookup-querryn tar en platt
/// concept-id-lista (chips från en sparad/recent-sökning) → cap måste spegla
/// summan av alla filterbara dimensioner. Fas C2 (CTO-dom (e)): legacy-Ssyk-
/// skälet utgick med reverse-lookup-migrationen, men ×4 BEHÅLLS — dims =
/// OccupationGroup + Municipality + Region + headroom för B2-dimensionerna
/// (employment_type/worktime_extent, resolverbara post re-ingest). Capen är
/// ett tak, inte en exakt summa — churn 4→3→5 vore poänglös. ×4 = 1600 med
/// MaxConceptIds=400. Säkert: O(n) in-memory dict-lookup, auth+rate-limited
/// (TaxonomyReadPolicy), per-element MaximumLength(32). CTO-dom 2026-06-09.
/// </para>
/// </summary>
public sealed class ResolveTaxonomyLabelsQueryValidator
    : AbstractValidator<ResolveTaxonomyLabelsQuery>
{
    // JobTech v2 concept-id-format — identiskt med ListJobAdsQueryValidator +
    // SearchCriteria (default-deny, Saltzer/Schroeder 1975). Charset-cappet
    // begränsar även den reflekterade id-strängen i svars-DTO:n (defense-in-
    // depth mot XSS-stuffing; FE:s render bär det primära ansvaret).
    // security-auditor 2026-06-09 Minor — symmetri med övriga concept-id-ytor.
    private const string ConceptIdPattern = @"^[A-Za-z0-9_-]{1,32}\z";

    // OccupationGroup + Municipality + Region + headroom (B2-dims post
    // re-ingest) = fyra MaxConceptIds-listor en chip-render kan materialisera.
    public static readonly int MaxConceptIdsPerCall = SearchCriteria.MaxConceptIds * 4;

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
            .Matches(ConceptIdPattern)   // speglar SearchCriteria concept-id-format
            .WithMessage("Concept-id måste vara en giltig JobTech concept-id (1-32 tecken, alfanumeriskt + _-).");
    }
}
