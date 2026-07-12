using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Applications.Commands.DeleteApplication;

/// <summary>
/// #782 (ADR 0104) — user-initiated per-application HARD delete. A convenience
/// delete for an application created by mistake / cleanup; DISTINCT from the
/// Withdrawn status transition (which keeps the record as a terminal state) and
/// from the account-erasure cascade (ADR 0024, whole-account Art. 17).
///
/// HARD (tracked <c>Remove</c>), not the soft-delete idiom of SavedSearch/Resume:
/// the UX copy promises irreversible removal ("Detta kan inte ångras"), and no
/// per-application sweeper reclaims a soft-deleted row — a soft delete would leave
/// plaintext (ManualPosting/AdSnapshot) + DEK-ciphertext rows resident indefinitely
/// (GDPR Art. 5(1)(c)/(e)). Mirrors <c>DeleteRecentSearchCommandHandler</c>'s tracked
/// <c>Remove</c> + audit. The child collections (FollowUps/Notes/StatusChanges) are
/// removed by the DB FK cascade (OnDelete.Cascade), so the root is loaded WITHOUT
/// <c>Include</c>. The delete + the audit row persist atomically in the pipeline's
/// UnitOfWork SaveChanges (tracked <c>Remove</c>, never <c>ExecuteDeleteAsync</c>,
/// which would commit immediately and break delete+audit atomicity).
///
/// Cross-tenant per ADR 0031: an id owned by another user is logged as a cross-user
/// attempt and surfaced as an identical NotFound (no enumeration oracle).
/// </summary>
public sealed class DeleteApplicationCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IFailedAccessLogger failedAccessLogger)
    : ICommandHandler<DeleteApplicationCommand, Result>
{
    public async ValueTask<Result> Handle(
        DeleteApplicationCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return Result.Failure(
                DomainError.Validation("Application.Unauthorized", "Användaren är inte autentiserad."));

        var jobSeekerId = await db.JobSeekers
            .AsNoTracking()
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (jobSeekerId == default)
            return Result.Failure(DomainError.NotFound("Application", command.Id));

        var applicationId = new Jobbliggaren.Domain.Applications.ApplicationId(command.Id);

        var application = await db.Applications
            .FirstOrDefaultAsync(
                a => a.Id == applicationId && a.JobSeekerId == jobSeekerId,
                cancellationToken);

        if (application is null)
        {
            // Failed-access-detection (ADR 0031): distinguish "unknown id" from
            // "belongs to another user". Client sees an identical NotFound.
            var exists = await db.Applications
                .AsNoTracking()
                .AnyAsync(a => a.Id == applicationId, cancellationToken);
            if (exists)
            {
                failedAccessLogger.LogCrossUserAttempt(
                    "Application", applicationId.Value, currentUser.UserId.Value, "DeleteApplication");
            }
            return Result.Failure(DomainError.NotFound("Application", command.Id));
        }

        // HARD delete — children (FollowUps/Notes/StatusChanges) cascade via the DB
        // FK (OnDelete.Cascade). Tracked Remove so the DELETE and the audit row commit
        // atomically in the pipeline UnitOfWork.
        db.Applications.Remove(application);
        return Result.Success();
    }
}
