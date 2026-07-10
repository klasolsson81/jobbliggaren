using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.JobAds.Commands.MarkJobsSeen;

/// <summary>
/// #293 (ADR 0042 Beslut E amendment) — the "markera jobblistan sedd"-command handler. Loads
/// the authenticated user's JobSeeker TRACKED and advances <c>LastSeenJobsAt</c> via the
/// monotonic <see cref="Domain.JobSeekers.JobSeeker.SetLastSeenJobs"/> (the UnitOfWorkBehavior
/// persists — this handler does not call SaveChanges itself, mirroring MarkMatchesSeen).
/// NO AI/LLM, no PII.
/// </summary>
public sealed class MarkJobsSeenCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : ICommandHandler<MarkJobsSeenCommand, Result>
{
    public async ValueTask<Result> Handle(
        MarkJobsSeenCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return Result.Failure(
                DomainError.Validation("JobSeeker.Unauthorized", "Användaren är inte autentiserad."));

        var jobSeeker = await db.JobSeekers
            .FirstOrDefaultAsync(js => js.UserId == currentUser.UserId.Value, cancellationToken);

        if (jobSeeker is null)
            return Result.Failure(
                DomainError.NotFound("JobSeeker", currentUser.UserId.Value));

        // #759 (sibling of #477 Low 4): advance the watermark to the window the user actually saw
        // (the max CreatedAt the FE sends), NOT clock-now — an ad ingested between the user's GET
        // and this POST has CreatedAt > seenThrough and stays flagged "Ny". Null (empty list /
        // deploy-skew) falls back to clock-now (the old behaviour); the aggregate clamps a future
        // value to now. Mirrors MarkMatchesSeenCommandHandler.
        var seenThrough = command.SeenThrough ?? clock.UtcNow;
        jobSeeker.SetLastSeenJobs(seenThrough, clock);
        return Result.Success();
    }
}
