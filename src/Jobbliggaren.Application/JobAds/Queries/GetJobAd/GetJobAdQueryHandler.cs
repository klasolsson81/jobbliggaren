using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.JobAds;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.JobAds.Queries.GetJobAd;

public sealed class GetJobAdQueryHandler(IAppDbContext db)
    : IQueryHandler<GetJobAdQuery, JobAdDto?>
{
    public async ValueTask<JobAdDto?> Handle(
        GetJobAdQuery query, CancellationToken cancellationToken)
    {
        return await db.JobAds
            .AsNoTracking()
            .Where(j => j.Id == new JobAdId(query.Id))
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
            .FirstOrDefaultAsync(cancellationToken);
    }
}
