using Jobbliggaren.Application.Common.Abstractions;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.JobSeekers.Queries.GetMyProfile;

public sealed class GetMyProfileQueryHandler(IAppDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetMyProfileQuery, JobSeekerProfileDto?>
{
    public async ValueTask<JobSeekerProfileDto?> Handle(
        GetMyProfileQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return null;

        var jobSeeker = await db.JobSeekers
            .AsNoTracking()
            .FirstOrDefaultAsync(js => js.UserId == currentUser.UserId.Value, cancellationToken);

        return jobSeeker is null ? null : JobSeekerProfileDto.FromDomain(jobSeeker);
    }
}
