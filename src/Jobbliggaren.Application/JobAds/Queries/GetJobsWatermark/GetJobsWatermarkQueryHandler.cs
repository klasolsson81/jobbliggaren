using Jobbliggaren.Application.Common.Abstractions;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.JobAds.Queries.GetJobsWatermark;

/// <summary>
/// #293 (ADR 0042 Beslut E amendment) — reads the authenticated user's <c>LastSeenJobsAt</c>
/// watermark off the JobSeeker (a first-class column, the sibling of <c>LastSeenMatchesAt</c>).
/// Owner-scoped (reads only the current user's watermark). No authenticated user / no JobSeeker
/// → null (honest "never seen"). <c>.AsNoTracking()</c> read (CLAUDE.md §3.6). NO AI/LLM.
/// </summary>
public sealed class GetJobsWatermarkQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser)
    : IQueryHandler<GetJobsWatermarkQuery, JobsWatermarkDto>
{
    public async ValueTask<JobsWatermarkDto> Handle(
        GetJobsWatermarkQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
            return JobsWatermarkDto.Empty;

        var lastSeen = await db.JobSeekers
            .AsNoTracking()
            .Where(js => js.UserId == userId)
            .Select(js => js.LastSeenJobsAt)
            .FirstOrDefaultAsync(cancellationToken);

        return new JobsWatermarkDto(lastSeen);
    }
}
