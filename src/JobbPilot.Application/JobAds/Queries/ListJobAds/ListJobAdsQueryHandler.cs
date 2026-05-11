using JobbPilot.Application.Common.Abstractions;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace JobbPilot.Application.JobAds.Queries.ListJobAds;

public sealed class ListJobAdsQueryHandler(IAppDbContext db)
    : IQueryHandler<ListJobAdsQuery, IReadOnlyList<JobAdDto>>
{
    // Defense-in-depth: hard cap mot DoS-vektor när JobAds-tabellen växer. Full
    // PagedResult<T>-retro-fit defererad till Fas 2 (JobTech-integration), då
    // query-params och URL-kontrakt designas mot JobTech-API:t. Se TD-56.
    // TODO(Fas 2): ersätts av PagedResult-retro-fit + IOptions<JobAdOptions>.
    private const int MaxItems = 500;

    public async ValueTask<IReadOnlyList<JobAdDto>> Handle(
        ListJobAdsQuery query, CancellationToken cancellationToken)
    {
        return await db.JobAds
            .AsNoTracking()
            .OrderByDescending(j => j.PublishedAt)
            .Take(MaxItems)
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
    }
}
