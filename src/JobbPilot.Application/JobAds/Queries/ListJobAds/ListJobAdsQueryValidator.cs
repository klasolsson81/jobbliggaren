using FluentValidation;
using JobbPilot.Domain.SavedSearches;

namespace JobbPilot.Application.JobAds.Queries.ListJobAds;

public sealed class ListJobAdsQueryValidator : AbstractValidator<ListJobAdsQuery>
{
    // JobTech v2 concept-id-format: kort sträng, alfanumerisk + `_-`, observerade
    // exempel ~12 tecken (`MVqp_eS8_kDZ`). Sätter 1-32 som defense-in-depth-yta
    // (Saltzer/Schroeder 1975 default-deny). CTO-rond 2026-05-13 Q7a/Q7b.
    // ADR 0042 Beslut B — multi: per-element-regex + maxantal-cap speglar
    // SearchCriteria.Create (Domain = sanningskälla; detta = defense-in-depth
    // pre-handler-yta, samma mönster som single-värde-validatorn hade).
    private const string ConceptIdPattern = @"^[A-Za-z0-9_-]{1,32}\z";

    public ListJobAdsQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);
        RuleFor(q => q.SortBy).IsInEnum();

        // Maxantal-cap (invariant 2) — IN(...)-blowup/jsonb-stuffing-DoS-tak.
        // Refererar Domain-konstanten (single source).
        //
        // ADR 0067 Beslut 1 — dimensioner OccupationGroup (ssyk-level-4/
        // yrkesgrupp, primärt yrke-filter) + Municipality (kommun) + Region.
        // Fas C2 (CTO-dom (e)): Ssyk-paramen (occupation-name) borttagen —
        // ?ssyk= är numera en obunden query-param som ignoreras av endpointen
        // tills Fas E byter FE-picker.
        RuleFor(q => q.OccupationGroup!)
            .Must(l => l.Count <= SearchCriteria.MaxConceptIds)
            .When(q => q.OccupationGroup is not null)
            .WithMessage($"Max {SearchCriteria.MaxConceptIds} yrkesgrupper per sökning.");

        RuleForEach(q => q.OccupationGroup)
            .Matches(ConceptIdPattern)
            .When(q => q.OccupationGroup is not null)
            .WithMessage("Yrkesgrupp måste vara en giltig JobTech concept-id (1-32 tecken, alfanumeriskt + _-).");

        RuleFor(q => q.Municipality!)
            .Must(l => l.Count <= SearchCriteria.MaxConceptIds)
            .When(q => q.Municipality is not null)
            .WithMessage($"Max {SearchCriteria.MaxConceptIds} kommuner per sökning.");

        RuleForEach(q => q.Municipality)
            .Matches(ConceptIdPattern)
            .When(q => q.Municipality is not null)
            .WithMessage("Kommun måste vara en giltig JobTech concept-id (1-32 tecken, alfanumeriskt + _-).");

        RuleFor(q => q.Region!)
            .Must(l => l.Count <= SearchCriteria.MaxConceptIds)
            .When(q => q.Region is not null)
            .WithMessage($"Max {SearchCriteria.MaxConceptIds} regioner per sökning.");

        RuleForEach(q => q.Region)
            .Matches(ConceptIdPattern)
            .When(q => q.Region is not null)
            .WithMessage("Region måste vara en giltig JobTech location-concept-id (1-32 tecken, alfanumeriskt + _-).");

        // q MinLength(2) hindrar `?q=a` (matchar närapå hela tabellen → DoS-yta).
        // MaxLength(100) räcker för normal söksträng + safety margin mot injection-
        // stuffing. CTO-rond 2026-05-13 Q7c.
        RuleFor(q => q.Q)
            .MinimumLength(2)
            .MaximumLength(100)
            .When(q => !string.IsNullOrWhiteSpace(q.Q))
            .WithMessage("Söktext måste vara 2-100 tecken.");

        // ADR 0042 Beslut D — relevans-sortering kräver söktext (fail-fast,
        // speglar SearchCriteria.Create-invarianten).
        RuleFor(q => q.Q)
            .NotEmpty()
            .When(q => q.SortBy == Domain.JobAds.JobAdSortBy.Relevance)
            .WithMessage("Relevans-sortering kräver en söktext.");
    }
}
