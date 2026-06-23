using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Queries.SearchSkills;
using Mediator;

namespace Jobbliggaren.Application.Matching.Queries.ResolveSkillLabels;

/// <summary>
/// ADR 0079 STEG 3 PR-C — resolves stored skill concept-ids to canonical labels via
/// <see cref="ISkillResolver.ResolveLabels"/> (the shared <c>SkillTaxonomyIndex</c>; no
/// parallel index, ADR 0076 Decision 6). Deterministic, no DB, no PII (concept-ids in,
/// taxonomy labels out). Returns DTOs (CQRS). Unknown ids drop → an empty list is a valid
/// "nothing resolved" result.
/// </summary>
public sealed class ResolveSkillLabelsQueryHandler(ISkillResolver skillResolver)
    : IQueryHandler<ResolveSkillLabelsQuery, IReadOnlyList<SkillOptionDto>>
{
    public ValueTask<IReadOnlyList<SkillOptionDto>> Handle(
        ResolveSkillLabelsQuery query, CancellationToken cancellationToken)
    {
        IReadOnlyList<SkillOptionDto> options = skillResolver
            .ResolveLabels(query.ConceptIds ?? [], cancellationToken)
            .Select(s => new SkillOptionDto(s.ConceptId, s.Label))
            .ToList();

        return ValueTask.FromResult(options);
    }
}
