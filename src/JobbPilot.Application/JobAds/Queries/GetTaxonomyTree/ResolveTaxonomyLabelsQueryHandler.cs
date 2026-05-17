using JobbPilot.Application.JobAds.Abstractions;
using Mediator;

namespace JobbPilot.Application.JobAds.Queries.GetTaxonomyTree;

/// <summary>
/// ADR 0043 — tunn adapter mot <see cref="ITaxonomyReadModel.ResolveLabelsAsync"/>.
/// Okänt concept-id → fallback-label i porten (graceful degradation, aldrig
/// throw). DoS-cap enforce:as i <c>ResolveTaxonomyLabelsQueryValidator</c>
/// FÖRE handlern (Validation-pipeline).
/// </summary>
public sealed class ResolveTaxonomyLabelsQueryHandler(ITaxonomyReadModel taxonomy)
    : IQueryHandler<ResolveTaxonomyLabelsQuery, IReadOnlyList<TaxonomyLabelDto>>
{
    public async ValueTask<IReadOnlyList<TaxonomyLabelDto>> Handle(
        ResolveTaxonomyLabelsQuery query, CancellationToken cancellationToken)
        => await taxonomy.ResolveLabelsAsync(query.ConceptIds, cancellationToken);
}
