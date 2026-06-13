using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.RecentJobSearches;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.RecentJobSearches.Commands.DeleteRecentSearch;

/// <summary>
/// ADR 0060 — hard-delete av en RecentJobSearch (auto-fångad sökning saknar
/// audit-trail-värdighet; soft-delete ej använd, jfr SavedSearch). Cross-tenant-
/// check per ADR 0031: skilj okänt id från forbidden, logga cross-user-attempt
/// men returnera samma NotFound-response.
/// </summary>
public sealed class DeleteRecentSearchCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IFailedAccessLogger failedAccessLogger)
    : ICommandHandler<DeleteRecentSearchCommand, Result>
{
    public async ValueTask<Result> Handle(
        DeleteRecentSearchCommand command, CancellationToken cancellationToken)
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
                    "DeleteRecentSearch");
            }
            return Result.Failure(DomainError.NotFound("RecentJobSearch", command.Id));
        }

        db.RecentJobSearches.Remove(recent);
        return Result.Success();
    }
}
