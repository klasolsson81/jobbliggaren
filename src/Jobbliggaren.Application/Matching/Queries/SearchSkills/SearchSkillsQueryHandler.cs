using Jobbliggaren.Application.Matching.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.Matching.Queries.SearchSkills;

/// <summary>
/// ADR 0079 STEG 3 PR-C — resolves a skill typeahead query to ranked, capped taxonomy
/// options via <see cref="ISkillResolver.Search"/> (the shared <c>SkillTaxonomyIndex</c>;
/// no parallel index, ADR 0076 Decision 6). Deterministic, no DB, no PII (the query is a
/// public typeahead string; the result is taxonomy labels). Returns DTOs, never a domain
/// object (CQRS). A blank/too-short query → empty list (graceful).
/// <para>
/// #277 — the ranked hits are then GROUPED by shared exact-label surface
/// (<see cref="ISkillResolver.GroupConceptIds"/>) so the ESCO + AF twins one literal
/// co-produces become ONE addable option carrying both member ids (a singleton carries one).
/// Rank order is preserved: the hits feed grouping in rank order, so the first group is the
/// top-ranked hit. [Deliberate divergence from the architect's "keep typeahead flat" note —
/// grouping the typeahead too keeps acceptance consistent with the saved-chip / propose paths.]
/// </para>
/// </summary>
public sealed class SearchSkillsQueryHandler(ISkillResolver skillResolver)
    : IQueryHandler<SearchSkillsQuery, IReadOnlyList<SkillOptionGroupDto>>
{
    public ValueTask<IReadOnlyList<SkillOptionGroupDto>> Handle(
        SearchSkillsQuery query, CancellationToken cancellationToken)
    {
        // Ranked hits (Search) → grouped by surface (GroupConceptIds), feeding the ids in rank
        // order so the grouped output preserves the typeahead ranking. The index guards the
        // no-drop invariant: every hit id lands in exactly one group.
        var rankedIds = skillResolver
            .Search(query.Query, cancellationToken)
            .Select(s => s.ConceptId)
            .ToList();

        IReadOnlyList<SkillOptionGroupDto> options = skillResolver
            .GroupConceptIds(rankedIds, cancellationToken)
            .Select(g => new SkillOptionGroupDto(g.CanonicalConceptId, g.Label, g.MemberConceptIds))
            .ToList();

        return ValueTask.FromResult(options);
    }
}
