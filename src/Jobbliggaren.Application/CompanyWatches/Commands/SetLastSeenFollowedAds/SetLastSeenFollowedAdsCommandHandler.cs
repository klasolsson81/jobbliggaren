using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.CompanyWatches.Commands.SetLastSeenFollowedAds;

/// <summary>
/// Bevakning F2 (#801, RF-6=6B) — advances <c>JobSeeker.LastSeenFollowedAdsAt</c>. Mirrors
/// <c>MarkMatchesSeenCommandHandler</c>'s auth + owner-scope shape: loads the JobSeeker TRACKED so
/// the <c>UnitOfWorkBehavior</c> persists the change (the handler never calls SaveChanges itself).
/// <c>SetLastSeenFollowedAds</c> is monotonic (the aggregate guards it), so a stale/duplicate call
/// never moves the watermark backwards. NO AI/LLM, no PII (a per-user behavioural timestamp only).
/// </summary>
public sealed class SetLastSeenFollowedAdsCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : ICommandHandler<SetLastSeenFollowedAdsCommand, Result>
{
    public async ValueTask<Result> Handle(
        SetLastSeenFollowedAdsCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return Result.Failure(
                DomainError.Validation("JobSeeker.Unauthorized", "Användaren är inte autentiserad."));

        var jobSeeker = await db.JobSeekers
            .FirstOrDefaultAsync(js => js.UserId == currentUser.UserId.Value, cancellationToken);

        if (jobSeeker is null)
            return Result.Failure(
                DomainError.NotFound("JobSeeker", currentUser.UserId.Value));

        // Advance to the window the caller acknowledged (or clock-now when the FE sends nothing —
        // the follows hub renders no individual hits to preserve, so clock-now is the honest
        // "acknowledged as of this visit"). The aggregate clamps a future value to now and is
        // monotonic (a stale call is a no-op).
        var seenThrough = command.SeenThrough ?? clock.UtcNow;
        jobSeeker.SetLastSeenFollowedAds(seenThrough, clock);
        return Result.Success();
    }
}
