using JobbPilot.Application.Common;
using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Domain.JobAds;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace JobbPilot.Application.JobAds.Queries.ListJobAds;

public sealed class ListJobAdsQueryHandler(IAppDbContext db)
    : IQueryHandler<ListJobAdsQuery, PagedResult<JobAdDto>>
{
    public async ValueTask<PagedResult<JobAdDto>> Handle(
        ListJobAdsQuery query, CancellationToken cancellationToken)
    {
        var baseQuery = ApplyFilters(db.JobAds.AsNoTracking(), query);

        // Separat count-query per CLAUDE.md §3.6. Filter appliceras före count
        // så totalen reflekterar filtrerad mängd, inte totalt antal annonser.
        var totalCount = await baseQuery.CountAsync(cancellationToken);

        var ordered = ApplySort(baseQuery, query.SortBy);

        var items = await ordered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(j => new JobAdDto(
                j.Id.Value,
                j.Title,
                j.Company.Name,
                j.Description,
                j.Url,
                j.Source.Value,
                j.Status.Value,
                j.PublishedAt,
                j.ExpiresAt,
                j.CreatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<JobAdDto>(items, totalCount, query.Page, query.PageSize);
    }

    // F2-P9 (TD-70, CTO-rond 2026-05-13). Filter via Postgres generated columns
    // (Q2-C) — ssyk_concept_id + region_concept_id är STORED-derived från
    // raw_payload, B-tree-indexerade för equality-lookup. q-filter via Like
    // + .ToLower() (Q3-A KISS för Fas 2-volym 5-15k rader). EF.Functions.ILike
    // skulle vara renare men ligger i Npgsql-extension → Application-Clean-Arch-
    // brott. EF.Functions.Like är Microsoft.EntityFrameworkCore (provider-
    // agnostiskt API).
    // Shadow-properties refereras via EF.Property<string?>(...) eftersom de
    // inte är top-level Domain-fält (Evans 2003 §14 ACL — JobTech-taxonomi
    // är inte JobbPilots ubiquitous language).
    private static IQueryable<JobAd> ApplyFilters(IQueryable<JobAd> source, ListJobAdsQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.Ssyk))
        {
            var ssyk = query.Ssyk;
            source = source.Where(j => EF.Property<string?>(j, "SsykConceptId") == ssyk);
        }

        if (!string.IsNullOrWhiteSpace(query.Region))
        {
            var region = query.Region;
            source = source.Where(j => EF.Property<string?>(j, "RegionConceptId") == region);
        }

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var pattern = $"%{query.Q.ToLowerInvariant()}%";
            // EF/Npgsql translaterar .ToLower() (utan culture-argument) till
            // SQL LOWER(col); .ToLowerInvariant() har ingen mapping i Npgsql
            // (verifierat 2026-05-13 via integration-test). CA1304-suppress:
            // detta är LINQ-translation till SQL, inte runtime-string-op —
            // culture är irrelevant (Postgres LOWER är locale-baserad i sin
            // egen rätt). Pattern lower:as redan invariant-side (C#).
#pragma warning disable CA1304, CA1311
            source = source.Where(j =>
                EF.Functions.Like(j.Title.ToLower(), pattern) ||
                EF.Functions.Like(j.Description.ToLower(), pattern));
#pragma warning restore CA1304, CA1311
        }

        return source;
    }

    // Alla enum-värden explicit listade + throw på unnamed (CS8524-disciplin).
    // ListJobAdsQueryValidator.IsInEnum() skyddar mot invalid values innan
    // handler, så throw är defense-in-depth — exception når inte runtime.
    // Vid framtida JobAdSortBy-tillägg: case missas → ArgumentOutOfRangeException
    // → integration-test fail (fail-fast extension-discipline).
    private static IQueryable<JobAd> ApplySort(IQueryable<JobAd> source, JobAdSortBy sortBy) =>
        sortBy switch
        {
            JobAdSortBy.PublishedAtDesc => source.OrderByDescending(j => j.PublishedAt).ThenBy(j => j.Id),
            JobAdSortBy.PublishedAtAsc => source.OrderBy(j => j.PublishedAt).ThenBy(j => j.Id),
            // NULL-ExpiresAt sorteras sist (har inget slut-datum = pågående).
            JobAdSortBy.ExpiresAtDesc =>
                source.OrderBy(j => j.ExpiresAt == null)
                      .ThenByDescending(j => j.ExpiresAt)
                      .ThenBy(j => j.Id),
            JobAdSortBy.ExpiresAtAsc =>
                source.OrderBy(j => j.ExpiresAt == null)
                      .ThenBy(j => j.ExpiresAt)
                      .ThenBy(j => j.Id),
            _ => throw new ArgumentOutOfRangeException(
                nameof(sortBy), sortBy, "Unknown JobAdSortBy — validator should reject."),
        };
}
