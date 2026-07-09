using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Domain.Resumes;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Resumes.Queries.GetResumes;

public sealed class GetResumesQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IRubricProvider rubricProvider)
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

        // Fas 4b PR-8 (ADR 0093 §D5(b), CTO-bind PR-8 Q1): the hub badge count is a
        // TRANSLATED aggregate over the DEK-free finding-status ledger, projected in the
        // SAME roundtrip as the page (entity + scalar projection — no second query to
        // drift against, no Include of the child collection, and never the review
        // engine on this path; ADR 0045 hub budget).
        var currentRubricVersion = rubricProvider.GetRubric().Version.ToString();

        // Hämta page tracked-fritt; SmartEnum-projektion till string + IReadOnlyList<string>
        // för TopSkills kräver in-memory-mapping efter ToListAsync (EF-translateability
        // bevisad pre-design; status quo bevaras — paging begränsar overhead).
        var page = await baseQuery
            .OrderByDescending(r => r.UpdatedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(r => new
            {
                Resume = r,
                OpenFindingCount = r.FindingStatuses.Count(f =>
                    f.RubricVersion == currentRubricVersion
                    && f.Status == ReviewFindingStatus.Open),
            })
            .ToListAsync(cancellationToken);

        var resumes = page.Select(x => new ResumeListItemDto(
            x.Resume.Id.Value,
            x.Resume.Name,
            x.Resume.Versions.Count(v => v.DeletedAt == null),
            x.Resume.CreatedAt,
            x.Resume.UpdatedAt,
            primaryResumeId is not null && x.Resume.Id == primaryResumeId,
            x.Resume.Language.Name,
            x.Resume.LatestRole,
            x.Resume.SectionCount,
            x.Resume.TopSkills.ToList(),
            // Null unless the ledger was reconciled at the CURRENT rubric version: "0"
            // may only ever mean reviewed-and-clean, never never-reviewed (§5 honesty;
            // ADR 0097 §5 version-non-carry — a stale stamp renders as "Granska").
            x.Resume.ReviewedRubricVersion == currentRubricVersion
                ? x.OpenFindingCount
                : null,
            x.Resume.Origin.Name,
            x.Resume.TemplateOptions.Template.Name)).ToList();

        return new PagedResult<ResumeListItemDto>(resumes, totalCount, query.Page, query.PageSize);
    }

    private static PagedResult<ResumeListItemDto> Empty(GetResumesQuery query) =>
        new(Array.Empty<ResumeListItemDto>(), 0, query.Page, query.PageSize);
}
