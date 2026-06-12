using FluentValidation;
using JobbPilot.Domain.SavedSearches;

namespace JobbPilot.Application.JobAds.Queries.GetFacetCounts;

/// <summary>
/// Speglar <c>ListJobAdsQueryValidator</c> exakt (defense-in-depth pre-handler-
/// yta; Domain <c>SearchCriteria</c> är sanningskälla för konstanterna).
/// <para>
/// <c>IsInEnum()</c> på Dimension är OBLIGATORISK — minimal-API:s enum-bindning
/// accepterar numeriska strängar utanför definierad mängd (<c>?dimension=7</c>
/// binder till <c>(FacetDimension)7</c>); utan regeln blir det 500 via
/// Infrastructure-switchens throw i stället för rent 400 (E2c-architect §1,
/// samma skäl som SortBy-regeln).
/// </para>
/// </summary>
public sealed class GetFacetCountsQueryValidator : AbstractValidator<GetFacetCountsQuery>
{
    // JobTech v2 concept-id-format (samma yta som ListJobAdsQueryValidator).
    private const string ConceptIdPattern = @"^[A-Za-z0-9_-]{1,32}\z";

    public GetFacetCountsQueryValidator()
    {
        RuleFor(q => q.Dimension).IsInEnum();

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

        // ADR 0067 Beslut 6 (Fas B2) — Klass 2 filterkontext, samma yta.
        RuleFor(q => q.EmploymentType!)
            .Must(l => l.Count <= SearchCriteria.MaxConceptIds)
            .When(q => q.EmploymentType is not null)
            .WithMessage($"Max {SearchCriteria.MaxConceptIds} anställningsformer per sökning.");

        RuleForEach(q => q.EmploymentType)
            .Matches(ConceptIdPattern)
            .When(q => q.EmploymentType is not null)
            .WithMessage("Anställningsform måste vara en giltig JobTech concept-id (1-32 tecken, alfanumeriskt + _-).");

        RuleFor(q => q.WorktimeExtent!)
            .Must(l => l.Count <= SearchCriteria.MaxConceptIds)
            .When(q => q.WorktimeExtent is not null)
            .WithMessage($"Max {SearchCriteria.MaxConceptIds} omfattningar per sökning.");

        RuleForEach(q => q.WorktimeExtent)
            .Matches(ConceptIdPattern)
            .When(q => q.WorktimeExtent is not null)
            .WithMessage("Omfattning måste vara en giltig JobTech concept-id (1-32 tecken, alfanumeriskt + _-).");

        // Samma Q-gränser som list-vägen (Domain-konstanter, single source) —
        // residual-konsistensen kräver att även valideringen är symmetrisk.
        RuleFor(q => q.Q)
            .MinimumLength(SearchCriteria.QMinLength)
            .MaximumLength(SearchCriteria.QMaxLength)
            .When(q => !string.IsNullOrWhiteSpace(q.Q))
            .WithMessage("Söktext måste vara 2-100 tecken.");
    }
}
