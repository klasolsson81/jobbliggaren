using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.SavedSearches;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.SavedSearches.Commands.MarkResultsSeen;

/// <summary>
/// #312 (ADR 0115) — advances <c>SavedSearch.ResultsSeenAt</c> for the caller's OWN saved search.
/// Mirrors <c>MarkMatchesSeenCommandHandler</c>'s auth + owner-scope shape, but per-SAVED-SEARCH
/// (the watermark lives on the aggregate, not on JobSeeker): loads the SavedSearch TRACKED so the
/// <c>UnitOfWorkBehavior</c> persists the change. <c>MarkResultsSeen</c> is monotonic (the aggregate
/// guards it), so a stale/duplicate call never rewinds the watermark. Cross-tenant access is denied
/// (404, no existence leak) + logged (ADR 0031, parity <c>RunSavedSearchQueryHandler</c>).
/// NO AI/LLM, no PII.
/// </summary>
public sealed class MarkSavedSearchResultsSeenCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IFailedAccessLogger failedAccessLogger,
    IDateTimeProvider clock)
    : ICommandHandler<MarkSavedSearchResultsSeenCommand, Result>
{
    public async ValueTask<Result> Handle(
        MarkSavedSearchResultsSeenCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return Result.Failure(
                DomainError.Validation("SavedSearch.Unauthorized", "Användaren är inte autentiserad."));

        var jobSeekerId = await db.JobSeekers
            .AsNoTracking()
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (jobSeekerId == default)
            return Result.Failure(DomainError.NotFound("SavedSearch", command.Id));

        var savedSearchId = new SavedSearchId(command.Id);

        // Owner-scoped load, TRACKED (UnitOfWorkBehavior persists the watermark advance).
        var savedSearch = await db.SavedSearches
            .FirstOrDefaultAsync(
                s => s.Id == savedSearchId && s.JobSeekerId == jobSeekerId, cancellationToken);

        if (savedSearch is null)
        {
            // Failed-access-detection (ADR 0031): skilj okänt id från cross-tenant. Båda → 404
            // (ingen existens-läcka); cross-tenant loggas.
            var exists = await db.SavedSearches
                .AsNoTracking()
                .AnyAsync(s => s.Id == savedSearchId, cancellationToken);
            if (exists)
            {
                failedAccessLogger.LogCrossUserAttempt(
                    "SavedSearch", savedSearchId.Value, currentUser.UserId.Value,
                    "MarkSavedSearchResultsSeen");
            }
            return Result.Failure(DomainError.NotFound("SavedSearch", command.Id));
        }

        // #477/#759 — advance to the window the user actually saw (max CreatedAt from FE), NOT
        // clock-now; null falls back to clock-now. The aggregate is monotonic + clamps future→now.
        var seenThrough = command.SeenThrough ?? clock.UtcNow;
        savedSearch.MarkResultsSeen(seenThrough, clock);
        return Result.Success();
    }
}
