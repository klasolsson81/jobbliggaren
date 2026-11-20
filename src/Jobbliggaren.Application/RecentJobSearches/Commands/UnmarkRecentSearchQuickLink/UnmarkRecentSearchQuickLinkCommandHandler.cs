using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.RecentJobSearches;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.RecentJobSearches.Commands.UnmarkRecentSearchQuickLink;

/// <summary>
/// ADR 0188 (#2265) — loads the user's own recent search tracked and clears its quick-link slot; the unit of work saves.
/// Another user's row answers the same 404 as an unknown one, and the attempt is logged (ADR 0031). A search that is no
/// quick link is a no-op that succeeds.
/// </summary>
public sealed class UnmarkRecentSearchQuickLinkCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IFailedAccessLogger failedAccessLogger,
    IDateTimeProvider clock)
    : ICommandHandler<UnmarkRecentSearchQuickLinkCommand, Result>
{
    public async ValueTask<Result> Handle(
        UnmarkRecentSearchQuickLinkCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return Result.Failure(
                DomainError.Validation("RecentJobSearch.Unauthorized", "Användaren är inte autentiserad."));

        var jobSeekerId = await db.JobSeekers
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (jobSeekerId == default)
            return Result.Failure(DomainError.NotFound("RecentJobSearch", command.Id));

        var recentId = new RecentJobSearchId(command.Id);

        var recent = await db.RecentJobSearches
            .Where(r => r.Id == recentId && r.JobSeekerId == jobSeekerId)
            .FirstOrDefaultAsync(cancellationToken);

        if (recent is null)
        {
            var exists = await db.RecentJobSearches
                .AsNoTracking()
                .AnyAsync(r => r.Id == recentId, cancellationToken);
            if (exists)
            {
                failedAccessLogger.LogCrossUserAttempt(
                    "RecentJobSearch", recentId.Value, currentUser.UserId.Value,
                    "UnmarkRecentSearchQuickLink");
            }
            return Result.Failure(DomainError.NotFound("RecentJobSearch", command.Id));
        }

        recent.UnmarkQuickLink(clock.UtcNow);
        return Result.Success();
    }
}
