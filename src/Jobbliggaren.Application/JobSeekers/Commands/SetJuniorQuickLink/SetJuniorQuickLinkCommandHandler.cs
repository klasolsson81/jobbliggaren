using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.JobSeekers.Commands.SetJuniorQuickLink;

/// <summary>
/// ADR 0188 (#2265) — loads the current user's JobSeeker tracked so <c>UnitOfWorkBehavior</c> persists the change, and
/// echoes its id for the audit row. Turning the link on asks <see cref="QuickLinkCap"/> first, and only when the link is
/// off: a repeated "on", or a replay, is a no-op that succeeds.
/// </summary>
public sealed class SetJuniorQuickLinkCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : ICommandHandler<SetJuniorQuickLinkCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(
        SetJuniorQuickLinkCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return Result.Failure<Guid>(
                DomainError.Validation("JobSeeker.Unauthorized", "Användaren är inte autentiserad."));

        var jobSeeker = await db.JobSeekers
            .FirstOrDefaultAsync(js => js.UserId == currentUser.UserId.Value, cancellationToken);

        if (jobSeeker is null)
            return Result.Failure<Guid>(
                DomainError.NotFound("JobSeeker", currentUser.UserId.Value));

        if (command.Enabled && jobSeeker.Preferences.JuniorQuickLinkOffAt is not null)
        {
            var marked = await db.RecentJobSearches
                .CountAsync(r => r.JobSeekerId == jobSeeker.Id && r.QuickLinkSlot != null, cancellationToken);
            var room = QuickLinkCap.EnsureRoomForOne(juniorLinkOn: false, marked);
            if (room.IsFailure)
                return Result.Failure<Guid>(room.Error);
        }

        jobSeeker.SetJuniorQuickLink(command.Enabled, clock);

        return Result.Success(jobSeeker.Id.Value);
    }
}
