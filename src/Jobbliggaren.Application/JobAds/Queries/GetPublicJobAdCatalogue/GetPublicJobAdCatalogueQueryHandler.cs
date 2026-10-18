using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Listing;
using Jobbliggaren.Application.JobAds.Publication;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.JobAds.Queries.GetPublicJobAdCatalogue;

/// <summary>
/// One page of the public catalogue (ADR 0176). Membership, order and count come from the listed-ad snapshot that also
/// serves the sitemap; each row's content is read now and classified again, so an ad that ended or was erased after
/// the snapshot's read is absent from its page at once and is never served from memory.
/// </summary>
public sealed class GetPublicJobAdCatalogueQueryHandler(
    IListedJobAdSnapshotReader reader, IAppDbContext db, ITaxonomyReadModel taxonomy, IDateTimeProvider clock)
    : IQueryHandler<GetPublicJobAdCatalogueQuery, PublicJobAdCataloguePage>
{
    public async ValueTask<PublicJobAdCataloguePage> Handle(
        GetPublicJobAdCatalogueQuery query, CancellationToken cancellationToken)
    {
        // Availability before the page range, so an outage never reads as a page that does not exist.
        var now = clock.UtcNow;
        var snapshot = reader.Current;
        if (snapshot is null || !snapshot.IsFreshAt(now))
            return new PublicJobAdCataloguePage.Unavailable();

        const int pageSize = PublicJobAdCatalogueLimits.PageSize;
        var lastPage = Math.Max(1, (snapshot.Count + pageSize - 1) / pageSize);
        if (query.Page > lastPage)
            return new PublicJobAdCataloguePage.NoSuchPage();

        var slice = snapshot.CatalogueOrder.Skip((query.Page - 1) * pageSize).Take(pageSize).ToList();
        if (slice.Count == 0)
            return Available([], snapshot.Count, query.Page);

        // List<JobAdId>.Contains on the key, composed with the publication predicate, translates on Postgres:
        // GetPublicJobAdCatalogueQueryHandlerTests (Testcontainers) is the oracle. A narrow row, never the entity, because
        // RawPayload carries the org.nr and the pre-scrub text.
        var typedIds = slice.Select(id => new JobAdId(id)).ToList();
        var rows = await db.JobAds
            .AsNoTracking()
            .Where(j => typedIds.Contains(j.Id))
            .Where(JobAdPublication.IsListed(now))
            .Select(j => new
            {
                Id = j.Id.Value,
                j.Title,
                CompanyName = j.Company.Name,
                j.MunicipalityConceptId,
                j.RegionConceptId,
                j.PublishedAt,
            })
            .ToListAsync(cancellationToken);

        var names = await PublicPlaceNames.ResolveAsync(
            taxonomy, rows.SelectMany(r => new[] { r.MunicipalityConceptId, r.RegionConceptId }), cancellationToken);

        // The snapshot's order, not the read's. A row the read did not return has ended or been erased since the
        // snapshot's read: it drops out, and nothing moves up to fill its place.
        var byId = rows.ToDictionary(r => r.Id);
        var items = slice
            .Where(byId.ContainsKey)
            .Select(id => byId[id])
            .Select(r => new PublicJobAdSummaryDto(
                r.Id,
                r.Title,
                r.CompanyName,
                PublicPlaceNames.NameOf(names, r.MunicipalityConceptId),
                PublicPlaceNames.NameOf(names, r.RegionConceptId),
                r.PublishedAt))
            .ToList();

        return Available(items, snapshot.Count, query.Page);
    }

    private static PublicJobAdCataloguePage.Available Available(
        IReadOnlyList<PublicJobAdSummaryDto> items, int count, int page) =>
        new(new PagedResult<PublicJobAdSummaryDto>(items, count, page, PublicJobAdCatalogueLimits.PageSize));
}
