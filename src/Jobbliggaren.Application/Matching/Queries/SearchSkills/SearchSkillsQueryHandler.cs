using Jobbliggaren.Application.Matching.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.Matching.Queries.SearchSkills;

/// <summary>
/// ADR 0079 STEG 3 PR-C — resolves a skill typeahead query to ranked, capped taxonomy
/// options via <see cref="ISkillResolver.Search"/> (the shared <c>SkillTaxonomyIndex</c>;
/// no parallel index, ADR 0076 Decision 6). Deterministic, no DB, no PII (the query is a
/// public typeahead string; the result is taxonomy labels). Returns DTOs, never a domain
/// object (CQRS). A blank/too-short query → empty list (graceful).
/// </summary>
public sealed class SearchSkillsQueryHandler(ISkillResolver skillResolver)
    : IQueryHandler<SearchSkillsQuery, IReadOnlyList<SkillOptionDto>>
{
    public ValueTask<IReadOnlyList<SkillOptionDto>> Handle(
        SearchSkillsQuery query, CancellationToken cancellationToken)
    {
        IReadOnlyList<SkillOptionDto> options = skillResolver
            .Search(query.Query, cancellationToken)
            .Select(s => new SkillOptionDto(s.ConceptId, s.Label))
            .ToList();

        return ValueTask.FromResult(options);
    }
}
