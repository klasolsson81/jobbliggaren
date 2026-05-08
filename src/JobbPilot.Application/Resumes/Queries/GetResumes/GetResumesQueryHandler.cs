using JobbPilot.Application.Common.Abstractions;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace JobbPilot.Application.Resumes.Queries.GetResumes;

public sealed class GetResumesQueryHandler(IAppDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetResumesQuery, IReadOnlyList<ResumeListItemDto>>
{
    public async ValueTask<IReadOnlyList<ResumeListItemDto>> Handle(
        GetResumesQuery query, CancellationToken cancellationToken)
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

        var resumes = await db.Resumes
            .AsNoTracking()
            .Where(r => r.JobSeekerId == jobSeekerId)
            .OrderByDescending(r => r.UpdatedAt)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(r => new ResumeListItemDto(
                r.Id.Value,
                r.Name,
                r.Versions.Count(v => v.DeletedAt == null),
                r.CreatedAt,
                r.UpdatedAt))
            .ToListAsync(cancellationToken);

        return resumes;
    }
}
