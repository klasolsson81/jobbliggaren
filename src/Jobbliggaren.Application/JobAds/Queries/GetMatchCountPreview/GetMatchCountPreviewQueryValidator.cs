using FluentValidation;
using Jobbliggaren.Domain.SavedSearches;

namespace Jobbliggaren.Application.JobAds.Queries.GetMatchCountPreview;

/// <summary>
/// Epik #526 (ADR 0089) — speglar <c>GetFacetCountsQueryValidator</c>/<c>ListJobAdsQueryValidator</c>
/// (defense-in-depth pre-handler-yta; Domain <see cref="SearchCriteria"/> är sanningskälla för
/// konstanterna). Detta är input-härdnings-gränsen som skyddar <c>ApplyFilter</c>:s IN-clause mot
/// en illvillig body: per-element concept-id-regex + per-lista-cap.
/// <para>
/// <b>Om personnummer:</b> concept-id-regexen avvisar FRITEXT och kapar längd (≤32 tecken) —
/// men den blockerar INTE en bar siffersträng (ett 10-/12-siffrigt personnummer matchar
/// <c>[A-Za-z0-9_-]{1,32}</c>). Personnummer-säkerheten vilar därför inte på validatorn utan på
/// de tre verkliga kontrollerna: svaret är int-only (ingen echo av utkastet), <c>LoggingBehavior</c>
/// loggar bara typnamn (ingen PII i loggar), och queryn persisterar inget (ingen SearchCriteria/
/// recent-search-rad). Validatorn är strukturell härdning, inte personnummer-grinden.
/// </para>
/// </summary>
public sealed class GetMatchCountPreviewQueryValidator : AbstractValidator<GetMatchCountPreviewQuery>
{
    // JobTech v2 concept-id-format (samma yta som ListJobAdsQueryValidator / GetFacetCounts).
    private const string ConceptIdPattern = @"^[A-Za-z0-9_-]{1,32}\z";

    public GetMatchCountPreviewQueryValidator()
    {
        RuleFor(q => q.OccupationGroups)
            .Must(l => l.Count <= SearchCriteria.MaxConceptIds)
            .WithMessage($"Max {SearchCriteria.MaxConceptIds} yrkesgrupper per sökning.");
        RuleForEach(q => q.OccupationGroups)
            .Matches(ConceptIdPattern)
            .WithMessage("Yrkesgrupp måste vara en giltig JobTech concept-id (1-32 tecken, alfanumeriskt + _-).");

        RuleFor(q => q.Regions)
            .Must(l => l.Count <= SearchCriteria.MaxConceptIds)
            .WithMessage($"Max {SearchCriteria.MaxConceptIds} regioner per sökning.");
        RuleForEach(q => q.Regions)
            .Matches(ConceptIdPattern)
            .WithMessage("Region måste vara en giltig JobTech location-concept-id (1-32 tecken, alfanumeriskt + _-).");

        RuleFor(q => q.Municipalities)
            .Must(l => l.Count <= SearchCriteria.MaxConceptIds)
            .WithMessage($"Max {SearchCriteria.MaxConceptIds} kommuner per sökning.");
        RuleForEach(q => q.Municipalities)
            .Matches(ConceptIdPattern)
            .WithMessage("Kommun måste vara en giltig JobTech concept-id (1-32 tecken, alfanumeriskt + _-).");

        RuleFor(q => q.EmploymentTypes)
            .Must(l => l.Count <= SearchCriteria.MaxConceptIds)
            .WithMessage($"Max {SearchCriteria.MaxConceptIds} anställningsformer per sökning.");
        RuleForEach(q => q.EmploymentTypes)
            .Matches(ConceptIdPattern)
            .WithMessage("Anställningsform måste vara en giltig JobTech concept-id (1-32 tecken, alfanumeriskt + _-).");
    }
}
