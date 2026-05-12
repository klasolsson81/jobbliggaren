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
        var baseQuery = db.JobAds.AsNoTracking();

        // Separat count-query per CLAUDE.md §3.6.
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
