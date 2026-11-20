using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Junior;
using Jobbliggaren.Application.JobAds.Publication;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Infrastructure.JobAds;

/// <summary>
/// <see cref="IJuniorItJobAdQuery"/> (ADR 0186, ADR 0187): the ads listed at <c>now</c> that <see cref="JuniorMembership"/>
/// admits, read per request. A narrow projection, never the entity, because <c>RawPayload</c> carries the pre-scrub text.
/// </summary>
internal sealed class JuniorItJobAdQuery(AppDbContext db, IJuniorFieldCatalog catalog) : IJuniorItJobAdQuery
{
    // ApplySort reads the search text only for Relevance, which this list never asks for.
    private static readonly ResolvedSearchText NoText = new(null, [], TaxonomyLabelMatchDto.Empty);

    public async ValueTask<IReadOnlyList<JuniorItMunicipalityCount>> CountByMunicipalityAsync(
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var groups = JuniorMembership.GroupsOf(await catalog.ResolveEnabledAsync(cancellationToken));

        return await db.JobAds
            .AsNoTracking()
            .Where(JobAdPublication.IsListed(now))
            .Where(JuniorMembership.Of(db.JuniorAssessments, groups))
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
        var groups = JuniorMembership.GroupsOf(await catalog.ResolveEnabledAsync(cancellationToken));

        var source = db.JobAds
            .AsNoTracking()
            .Where(JobAdPublication.IsListed(now))
            .Where(JuniorMembership.Of(db.JuniorAssessments, groups));

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
