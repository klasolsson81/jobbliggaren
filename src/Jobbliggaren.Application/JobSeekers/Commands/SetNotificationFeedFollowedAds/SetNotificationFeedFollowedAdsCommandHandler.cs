using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.JobSeekers.Commands.SetNotificationFeedFollowedAds;

/// <summary>
/// ADR 0168 (#2130) — loads the current user's JobSeeker tracked so <c>UnitOfWorkBehavior</c> persists the change, and
/// echoes its id for the audit row.
/// </summary>
public sealed class SetNotificationFeedFollowedAdsCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : ICommandHandler<SetNotificationFeedFollowedAdsCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(
        SetNotificationFeedFollowedAdsCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return Result.Failure<Guid>(
                DomainError.Validation("JobSeeker.Unauthorized", "Användaren är inte autentiserad."));

        var jobSeeker = await db.JobSeekers
            .FirstOrDefaultAsync(js => js.UserId == currentUser.UserId.Value, cancellationToken);

        if (jobSeeker is null)
            return Result.Failure<Guid>(
                DomainError.NotFound("JobSeeker", currentUser.UserId.Value));

        jobSeeker.SetNotificationFeedFollowedAds(command.Enabled, clock);

        return Result.Success(jobSeeker.Id.Value);
    }
}
