using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Matching.Commands.MarkMatchesSeen;

/// <summary>
/// ADR 0080 Vag 4 PR-5 — advances <c>JobSeeker.LastSeenMatchesAt</c> to now. Mirrors
/// <c>SetMatchPreferencesCommandHandler</c>'s auth + owner-scope shape: loads the JobSeeker
/// TRACKED so the <c>UnitOfWorkBehavior</c> persists the change. <c>SetLastSeenMatches</c> is
/// monotonic (the aggregate guards it), so a stale/duplicate call never moves the watermark
/// backwards. NO AI/LLM, no PII.
/// </summary>
public sealed class MarkMatchesSeenCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : ICommandHandler<MarkMatchesSeenCommand, Result>
{
    public async ValueTask<Result> Handle(
        MarkMatchesSeenCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return Result.Failure(
                DomainError.Validation("JobSeeker.Unauthorized", "Användaren är inte autentiserad."));

        var jobSeeker = await db.JobSeekers
            .FirstOrDefaultAsync(js => js.UserId == currentUser.UserId.Value, cancellationToken);

        if (jobSeeker is null)
            return Result.Failure(
                DomainError.NotFound("JobSeeker", currentUser.UserId.Value));

        // #477 Low: advance the watermark to the window the user actually saw (the max CreatedAt
        // the FE sends), NOT clock-now — a match created between the user's GET and this POST has
        // CreatedAt > seenThrough and stays flagged "nya". Null (empty list / deploy-skew) falls
        // back to clock-now (the old behaviour); the aggregate clamps a future value to now.
        var seenThrough = command.SeenThrough ?? clock.UtcNow;
        jobSeeker.SetLastSeenMatches(seenThrough, clock);
        return Result.Success();
    }
}
