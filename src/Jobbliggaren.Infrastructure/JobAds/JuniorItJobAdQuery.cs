using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Publication;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.JobAds.JuniorIt;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Infrastructure.JobAds;

/// <summary>
/// <see cref="IJuniorItJobAdQuery"/> (ADR 0186): the listed ads <see cref="JuniorItPredicate"/> admits, read per request.
/// A narrow projection, never the entity, because <c>RawPayload</c> carries the pre-scrub text.
/// </summary>
internal sealed class JuniorItJobAdQuery(AppDbContext db, JuniorItSetResolver resolver) : IJuniorItJobAdQuery
{
    // ApplySort reads the search text only for Relevance, which this list never asks for.
    private static readonly ResolvedSearchText NoText = new(null, [], TaxonomyLabelMatchDto.Empty);

    public async ValueTask<IReadOnlyList<JuniorItMunicipalityCount>> CountByMunicipalityAsync(
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var set = await resolver.ResolveAsync(cancellationToken);

        return await db.JobAds
            .AsNoTracking()
            .Where(JobAdPublication.IsListed(now))
            .Where(JuniorItPredicate.Matches(set))
            .GroupBy(j => j.MunicipalityConceptId)
            .Select(g => new JuniorItMunicipalityCount(g.Key, g.Count()))
            .ToListAsync(cancellationToken);
    }

    public async ValueTask<IReadOnlyList<JuniorItJobAdRow>> ReadPageAsync(
        DateTimeOffset now,
        string? municipalityConceptId,
        JobAdSortBy sortBy,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var set = await resolver.ResolveAsync(cancellationToken);

        var source = db.JobAds
            .AsNoTracking()
            .Where(JobAdPublication.IsListed(now))
            .Where(JuniorItPredicate.Matches(set));

        if (municipalityConceptId is { } kommun)
            source = source.Where(j => j.MunicipalityConceptId == kommun);

        return await JobAdSearchComposition.ApplySort(source, sortBy, NoText)
            .Skip(skip)
            .Take(take)
            .Select(j => new JuniorItJobAdRow(
                j.Id.Value,
                j.Title,
                j.Company.Name,
                j.MunicipalityConceptId,
                j.RegionConceptId,
                j.PublishedAt,
                j.ExpiresAt))
            .ToListAsync(cancellationToken);
    }
}
