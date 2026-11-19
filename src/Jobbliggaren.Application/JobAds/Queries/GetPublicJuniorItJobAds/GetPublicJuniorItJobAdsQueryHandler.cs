using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.JobAds.Queries.GetPublicJuniorItJobAds;

/// <summary>
/// One page of the open junior IT list (ADR 0186). Membership is decided per request from the ads listed now, so an ad
/// that closes, is removed, expires or is erased is absent from the next read with nothing done by hand.
/// </summary>
public sealed class GetPublicJuniorItJobAdsQueryHandler(
    IJuniorItJobAdQuery juniorIt, ITaxonomyReadModel taxonomy, IDateTimeProvider clock)
    : IQueryHandler<GetPublicJuniorItJobAdsQuery, PublicJuniorItJobAdsPage>
{
    public async ValueTask<PublicJuniorItJobAdsPage> Handle(
        GetPublicJuniorItJobAdsQuery query, CancellationToken cancellationToken)
    {
        // One instant for both reads, so the count and the page describe the same list.
        var now = clock.UtcNow;
        var counts = await juniorIt.CountByMunicipalityAsync(now, cancellationToken);

        var total = query.Municipality is { } chosen
            ? counts.Where(c => string.Equals(c.MunicipalityConceptId, chosen, StringComparison.Ordinal)).Sum(c => c.Count)
            : counts.Sum(c => c.Count);

        const int pageSize = PublicJuniorItJobAdLimits.PageSize;
        var lastPage = Math.Max(1, (total + pageSize - 1) / pageSize);
        if (query.Page > lastPage)
            return new PublicJuniorItJobAdsPage.NoSuchPage();

        var rows = total == 0
            ? []
            : await juniorIt.ReadPageAsync(
                now, query.Municipality, query.SortBy, (query.Page - 1) * pageSize, pageSize, cancellationToken);

        var names = await PublicTaxonomyNames.ResolveAsync(
            taxonomy,
            rows.SelectMany(r => new[] { r.MunicipalityConceptId, r.RegionConceptId })
                .Concat(counts.Select(c => c.MunicipalityConceptId)),
            cancellationToken);

        var items = rows
            .Select(r => new PublicJuniorItJobAdDto(
                r.Id,
                r.Title,
                r.CompanyName,
                PublicTaxonomyNames.NameOf(names, r.MunicipalityConceptId),
                PublicTaxonomyNames.NameOf(names, r.RegionConceptId),
                r.PublishedAt,
                r.ExpiresAt))
            .ToList();

        // A kommun without a name in the taxonomy cannot be offered by name; its ads still count in the total.
        var places = counts
            .Where(c => c.MunicipalityConceptId is not null)
            .Select(c => (Count: c, Name: PublicTaxonomyNames.NameOf(names, c.MunicipalityConceptId)))
            .Where(p => p.Name is not null)
            .Select(p => new PublicJuniorItPlaceDto(p.Count.MunicipalityConceptId!, p.Name!, p.Count.Count))
            .OrderByDescending(p => p.Count)
            .ThenBy(p => p.Name, StringComparer.Ordinal)
            .ToList();

        return new PublicJuniorItJobAdsPage.Available(
            new PagedResult<PublicJuniorItJobAdDto>(items, total, query.Page, pageSize), places);
    }
}
