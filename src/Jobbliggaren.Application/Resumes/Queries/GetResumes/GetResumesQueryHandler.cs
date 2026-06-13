using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.Common.Abstractions;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Resumes.Queries.GetResumes;

public sealed class GetResumesQueryHandler(IAppDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetResumesQuery, PagedResult<ResumeListItemDto>>
{
    public async ValueTask<PagedResult<ResumeListItemDto>> Handle(
        GetResumesQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return Empty(query);

        // Hämta jobSeeker-Id + PrimaryResumeId i ett steg (en query).
        var jobSeekerInfo = await db.JobSeekers
            .AsNoTracking()
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => new { js.Id, js.PrimaryResumeId })
            .FirstOrDefaultAsync(cancellationToken);

        if (jobSeekerInfo is null)
            return Empty(query);

        var primaryResumeId = jobSeekerInfo.PrimaryResumeId;
        var jobSeekerId = jobSeekerInfo.Id;

        var baseQuery = db.Resumes
            .AsNoTracking()
            .Where(r => r.JobSeekerId == jobSeekerId);

        // Separat count-query per CLAUDE.md §3.6.
        var totalCount = await baseQuery.CountAsync(cancellationToken);

        // Hämta page tracked-fritt; SmartEnum-projektion till string + IReadOnlyList<string>
        // för TopSkills kräver in-memory-mapping efter ToListAsync (EF-translateability
        // bevisad pre-design; status quo bevaras — paging begränsar overhead).
        var page = await baseQuery
            .OrderByDescending(r => r.UpdatedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        var resumes = page.Select(r => new ResumeListItemDto(
            r.Id.Value,
            r.Name,
            r.Versions.Count(v => v.DeletedAt == null),
            r.CreatedAt,
            r.UpdatedAt,
            primaryResumeId is not null && r.Id == primaryResumeId,
            r.Language.Name,
            r.LatestRole,
            r.SectionCount,
            r.TopSkills.ToList())).ToList();

        return new PagedResult<ResumeListItemDto>(resumes, totalCount, query.Page, query.PageSize);
    }

    private static PagedResult<ResumeListItemDto> Empty(GetResumesQuery query) =>
        new(Array.Empty<ResumeListItemDto>(), 0, query.Page, query.PageSize);
}
