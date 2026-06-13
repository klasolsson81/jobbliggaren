using Jobbliggaren.Application.JobAds.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.JobAds.Queries.GetTaxonomyTree;

/// <summary>
/// ADR 0043 — tunn adapter mot <see cref="ITaxonomyReadModel"/> (speglar
/// <c>SuggestJobAdTermsQueryHandler</c>). Ingen Npgsql/EF i Application —
/// ACL:n äger snapshot-läsningen (SPOT, Hunt/Thomas DRY; samma
/// port-inkapsling som <c>IJobSource</c>).
/// </summary>
public sealed class GetTaxonomyTreeQueryHandler(ITaxonomyReadModel taxonomy)
    : IQueryHandler<GetTaxonomyTreeQuery, TaxonomyTreeDto>
{
    public async ValueTask<TaxonomyTreeDto> Handle(
        GetTaxonomyTreeQuery query, CancellationToken cancellationToken)
        => await taxonomy.GetTreeAsync(cancellationToken);
}
