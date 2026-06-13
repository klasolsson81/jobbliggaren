using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.JobAds;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.UserStatus.Queries.HasApplied;

/// <summary>
/// ADR 0063 — handler för single has-applied-check (modal-footer initial-state).
/// </summary>
public sealed class HasAppliedQueryHandler(IAppDbContext db, ICurrentUser currentUser)
    : IQueryHandler<HasAppliedQuery, bool>
{
    public async ValueTask<bool> Handle(HasAppliedQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return false;

        var jobSeekerId = await db.JobSeekers
            .AsNoTracking()
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (jobSeekerId == default)
            return false;

        var jobAdId = new JobAdId(query.JobAdId);

        return await db.Applications
            .AsNoTracking()
            .AnyAsync(
                a => a.JobSeekerId == jobSeekerId && a.JobAdId == jobAdId,
                cancellationToken);
    }
}
