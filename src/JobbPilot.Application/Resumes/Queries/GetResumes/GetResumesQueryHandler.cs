using JobbPilot.Application.Common;
using JobbPilot.Application.Common.Abstractions;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace JobbPilot.Application.Resumes.Queries.GetResumes;

public sealed class GetResumesQueryHandler(IAppDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetResumesQuery, PagedResult<ResumeListItemDto>>
{
    public async ValueTask<PagedResult<ResumeListItemDto>> Handle(
        GetResumesQuery query, CancellationToken cancellationToken)
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

        var baseQuery = db.Resumes
            .AsNoTracking()
            .Where(r => r.JobSeekerId == jobSeekerId);

        // Separat count-query per CLAUDE.md §3.6.
        var totalCount = await baseQuery.CountAsync(cancellationToken);

        var resumes = await baseQuery
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

        return new PagedResult<ResumeListItemDto>(resumes, totalCount, query.PageNumber, query.PageSize);
    }

    private static PagedResult<ResumeListItemDto> Empty(GetResumesQuery query) =>
        new(Array.Empty<ResumeListItemDto>(), 0, query.PageNumber, query.PageSize);
}
