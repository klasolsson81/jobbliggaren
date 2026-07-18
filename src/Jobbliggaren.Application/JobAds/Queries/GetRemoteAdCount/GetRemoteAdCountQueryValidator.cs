using FluentValidation;
using Jobbliggaren.Domain.SavedSearches;

namespace Jobbliggaren.Application.JobAds.Queries.GetRemoteAdCount;

/// <summary>
/// #551 PR-B D7 — mirrors <c>GetFacetCountsQueryValidator</c> for the non-location
/// filter surface (defense-in-depth pre-handler hardening of <c>ApplyFilter</c>'s
/// IN-clause; Domain <see cref="SearchCriteria"/> owns the constants). No location
/// axis: this query does not carry Municipality/Region (D7 structural exclusion).
/// <para>
/// <b>On personnummer:</b> the concept-id regex rejects free text and caps length but
/// does NOT block a bare digit string — personnummer safety rests on the same three real
/// controls as the sibling count queries (int-only response, no PII in logs, no
/// persistence), not the validator (parity <c>GetMatchCountPreviewQueryValidator</c>).
/// </para>
/// </summary>
public sealed class GetRemoteAdCountQueryValidator : AbstractValidator<GetRemoteAdCountQuery>
{
    // JobTech v2 concept-id-format (samma yta som GetFacetCountsQueryValidator).
    private const string ConceptIdPattern = @"^[A-Za-z0-9_-]{1,32}\z";

    public GetRemoteAdCountQueryValidator()
    {
        RuleFor(q => q.OccupationGroup!)
            .Must(l => l.Count <= SearchCriteria.MaxConceptIds)
            .When(q => q.OccupationGroup is not null)
            .WithMessage($"Max {SearchCriteria.MaxConceptIds} yrkesgrupper per sökning.");

        RuleForEach(q => q.OccupationGroup)
            .Matches(ConceptIdPattern)
            .When(q => q.OccupationGroup is not null)
            .WithMessage("Yrkesgrupp måste vara en giltig JobTech concept-id (1-32 tecken, alfanumeriskt + _-).");

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

        // Samma Q-gränser som list-/facet-vägen (Domain-konstanter, single source) —
        // residual-konsistensen kräver symmetrisk validering.
        RuleFor(q => q.Q)
            .MinimumLength(SearchCriteria.QMinLength)
            .MaximumLength(SearchCriteria.QMaxLength)
            .When(q => !string.IsNullOrWhiteSpace(q.Q))
            .WithMessage("Söktext måste vara 2-100 tecken.");
    }
}
