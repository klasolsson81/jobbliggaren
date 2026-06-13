using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.SavedSearches;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.SavedSearches.Commands.DeleteSavedSearch;

public sealed class DeleteSavedSearchCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IFailedAccessLogger failedAccessLogger)
    : ICommandHandler<DeleteSavedSearchCommand, Result>
{
    public async ValueTask<Result> Handle(
        DeleteSavedSearchCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return Result.Failure(
                DomainError.Validation("SavedSearch.Unauthorized", "Användaren är inte autentiserad."));

        var jobSeekerId = await db.JobSeekers
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (jobSeekerId == default)
            return Result.Failure(DomainError.NotFound("SavedSearch", command.Id));

        var savedSearchId = new SavedSearchId(command.Id);

        var savedSearch = await db.SavedSearches
            .Where(s => s.Id == savedSearchId && s.JobSeekerId == jobSeekerId)
            .FirstOrDefaultAsync(cancellationToken);

        if (savedSearch is null)
        {
            // Failed-access-detection (ADR 0031): skilj okänt id från cross-tenant.
            var exists = await db.SavedSearches
                .AsNoTracking()
                .AnyAsync(s => s.Id == savedSearchId, cancellationToken);
            if (exists)
            {
                failedAccessLogger.LogCrossUserAttempt(
                    "SavedSearch", savedSearchId.Value, currentUser.UserId.Value,
                    "DeleteSavedSearch");
            }
            return Result.Failure(DomainError.NotFound("SavedSearch", command.Id));
        }

        // Idempotent — SoftDelete no-op:ar om redan raderad (ingen ny audit-rad
        // bör skrivas; ADR 0024 D4-mönster). AuditBehavior skriver ändå raden
        // vid success, vilket är acceptabelt: en upprepad DELETE är sällsynt
        // och en idempotent audit-rad är inte vilseledande (samma aggregat-id).
        savedSearch.SoftDelete(clock);

        return Result.Success();
    }
}
