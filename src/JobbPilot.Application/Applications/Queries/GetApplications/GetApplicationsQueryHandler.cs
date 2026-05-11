using JobbPilot.Application.Common;
using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Domain.Applications;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace JobbPilot.Application.Applications.Queries.GetApplications;

public sealed class GetApplicationsQueryHandler(IAppDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetApplicationsQuery, PagedResult<ApplicationDto>>
{
    public async ValueTask<PagedResult<ApplicationDto>> Handle(
        GetApplicationsQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return Empty(query);

        var jobSeekerId = await db.JobSeekers
            .AsNoTracking()
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (jobSeekerId == default)
            return Empty(query);

        var baseQuery = db.Applications
            .AsNoTracking()
            .Where(a => a.JobSeekerId == jobSeekerId);

        if (query.Status is not null &&
            ApplicationStatus.TryFromName(query.Status, out var status))
        {
            baseQuery = baseQuery.Where(a => a.Status == status);
        }

        // Separat count-query per CLAUDE.md §3.6 — projection-fri count är effektivare
        // än materialisering + Count() och låter EF generera SELECT COUNT(*).
        var totalCount = await baseQuery.CountAsync(cancellationToken);

        var apps = await baseQuery
            .OrderByDescending(a => a.UpdatedAt)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        var items = apps.Select(a => new ApplicationDto(
            a.Id.Value,
            a.JobSeekerId.Value,
            a.JobAdId == null ? (Guid?)null : a.JobAdId.Value.Value,
            a.Status.Name,
            a.CreatedAt,
            a.UpdatedAt)).ToList();

        return new PagedResult<ApplicationDto>(items, totalCount, query.PageNumber, query.PageSize);
    }

    private static PagedResult<ApplicationDto> Empty(GetApplicationsQuery query) =>
        new(Array.Empty<ApplicationDto>(), 0, query.PageNumber, query.PageSize);
}
