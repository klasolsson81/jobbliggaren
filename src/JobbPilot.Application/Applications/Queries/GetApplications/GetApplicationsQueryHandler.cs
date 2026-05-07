using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Domain.Applications;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace JobbPilot.Application.Applications.Queries.GetApplications;

public sealed class GetApplicationsQueryHandler(IAppDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetApplicationsQuery, IReadOnlyList<ApplicationDto>>
{
    public async ValueTask<IReadOnlyList<ApplicationDto>> Handle(
        GetApplicationsQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return [];

        var jobSeekerId = await db.JobSeekers
            .AsNoTracking()
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (jobSeekerId == default)
            return [];

        var q = db.Applications
            .AsNoTracking()
            .Where(a => a.JobSeekerId == jobSeekerId);

        if (query.Status is not null &&
            ApplicationStatus.TryFromName(query.Status, out var status))
        {
            q = q.Where(a => a.Status == status);
        }

        var apps = await q
            .OrderByDescending(a => a.UpdatedAt)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return apps.Select(a => new ApplicationDto(
            a.Id.Value,
            a.JobSeekerId.Value,
            a.JobAdId == null ? (Guid?)null : a.JobAdId.Value.Value,
            a.Status.Name,
            a.CreatedAt,
            a.UpdatedAt)).ToList();
    }
}
