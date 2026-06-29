using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Queries.SearchSkills;
using Mediator;

namespace Jobbliggaren.Application.Matching.Queries.ResolveSkillLabels;

/// <summary>
/// ADR 0079 STEG 3 PR-C — resolves stored skill concept-ids to canonical labels via the shared
/// <c>SkillTaxonomyIndex</c> (no parallel index, ADR 0076 Decision 6). Deterministic, no DB, no
/// PII (concept-ids in, taxonomy labels out). Returns DTOs (CQRS). Unknown/removed ids are dropped
/// silently (graceful — a stale concept never crashes the read, preserved from PR-C); an empty
/// list is a valid "nothing resolved" result.
/// <para>
/// #277 — the KNOWN ids are then GROUPED by shared exact-label surface
/// (<see cref="ISkillResolver.GroupConceptIds"/>), so a saved ESCO + AF twin-pair renders as ONE
/// chip carrying both member ids on cold load (a singleton concept → a one-member group). The
/// known-id pre-filter (<see cref="ISkillResolver.ResolveLabels"/>) keeps the documented
/// unknown-drop UX while the grouping helper's no-drop invariant holds over the known set.
/// </para>
/// </summary>
public sealed class ResolveSkillLabelsQueryHandler(ISkillResolver skillResolver)
    : IQueryHandler<ResolveSkillLabelsQuery, IReadOnlyList<SkillOptionGroupDto>>
{
    public ValueTask<IReadOnlyList<SkillOptionGroupDto>> Handle(
        ResolveSkillLabelsQuery query, CancellationToken cancellationToken)
    {
        // Drop unknown/removed ids first (graceful, ADR 0079 PR-C), then group the KNOWN ids by
        // surface so a saved twin-pair collapses to one chip. Feeding only known ids keeps the
        // grouping helper's no-drop invariant over a clean universe (no opaque-id chips surface).
        var knownIds = skillResolver
            .ResolveLabels(query.ConceptIds ?? [], cancellationToken)
            .Select(s => s.ConceptId)
            .ToList();

        IReadOnlyList<SkillOptionGroupDto> options = skillResolver
            .GroupConceptIds(knownIds, cancellationToken)
            .Select(g => new SkillOptionGroupDto(g.CanonicalConceptId, g.Label, g.MemberConceptIds))
            .ToList();

        return ValueTask.FromResult(options);
    }
}
