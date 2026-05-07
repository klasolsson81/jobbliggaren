using JobbPilot.Application.Common.Abstractions;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace JobbPilot.Application.Applications.Queries.GetPipeline;

public sealed class GetPipelineQueryHandler(IAppDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetPipelineQuery, IReadOnlyList<PipelineGroupDto>>
{
    public async ValueTask<IReadOnlyList<PipelineGroupDto>> Handle(
        GetPipelineQuery query, CancellationToken cancellationToken)
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

        var apps = await db.Applications
            .AsNoTracking()
            .Where(a => a.JobSeekerId == jobSeekerId)
            .OrderByDescending(a => a.UpdatedAt)
            .Take(500) // TD-8: pipeline är kanban-vy, inte paginerad lista — 500 som skyddsventil
            .ToListAsync(cancellationToken);

        return apps
            .GroupBy(a => a.Status.Name)
            .Select(g => new PipelineGroupDto(
                g.Key,
                g.Count(),
                g.Select(a => new ApplicationDto(
                    a.Id.Value,
                    a.JobSeekerId.Value,
                    a.JobAdId == null ? (Guid?)null : a.JobAdId.Value.Value,
                    a.Status.Name,
                    a.CreatedAt,
                    a.UpdatedAt)).ToList()))
            .ToList();
    }
}
