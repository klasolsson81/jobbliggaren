using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Resumes;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Resumes.Queries.GetResumeById;

public sealed class GetResumeByIdQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IFailedAccessLogger failedAccessLogger)
    : IQueryHandler<GetResumeByIdQuery, ResumeDetailDto?>
{
    public async ValueTask<ResumeDetailDto?> Handle(
        GetResumeByIdQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return null;

        var jobSeekerId = await db.JobSeekers
            .AsNoTracking()
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (jobSeekerId == default)
            return null;

        var resumeId = new ResumeId(query.Id);
        var resume = await db.Resumes
            .AsNoTracking()
            .Include(r => r.Versions)
            .Where(r => r.Id == resumeId && r.JobSeekerId == jobSeekerId)
            .FirstOrDefaultAsync(cancellationToken);

        if (resume is null)
        {
            var exists = await db.Resumes
                .AsNoTracking()
                .AnyAsync(r => r.Id == resumeId, cancellationToken);
            if (exists)
            {
                failedAccessLogger.LogCrossUserAttempt(
                    "Resume", resumeId.Value, currentUser.UserId.Value, "GetResumeById");
            }
            return null;
        }

        return resume.ToDetailDto();
    }
}
