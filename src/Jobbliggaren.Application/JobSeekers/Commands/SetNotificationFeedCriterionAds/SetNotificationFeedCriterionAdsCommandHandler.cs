using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.JobSeekers.Commands.SetNotificationFeedCriterionAds;

/// <summary>
/// ADR 0175 D8 (#1663) — loads the current user's JobSeeker tracked so <c>UnitOfWorkBehavior</c> persists the change, and
/// echoes its id for the audit row.
/// </summary>
public sealed class SetNotificationFeedCriterionAdsCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : ICommandHandler<SetNotificationFeedCriterionAdsCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(
        SetNotificationFeedCriterionAdsCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return Result.Failure<Guid>(
                DomainError.Validation("JobSeeker.Unauthorized", "Användaren är inte autentiserad."));

        var jobSeeker = await db.JobSeekers
            .FirstOrDefaultAsync(js => js.UserId == currentUser.UserId.Value, cancellationToken);

        if (jobSeeker is null)
            return Result.Failure<Guid>(
                DomainError.NotFound("JobSeeker", currentUser.UserId.Value));

        jobSeeker.SetNotificationFeedCriterionAds(command.Enabled, clock);

        return Result.Success(jobSeeker.Id.Value);
    }
}
