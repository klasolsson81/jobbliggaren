using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Resumes;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Applications.Commands.AttachResumeVersion;

/// <summary>
/// F4-11: attaches the CV version used for an application. Enforces the cross-user
/// invariant (BUILD §5.3) in the handler — the aggregate cannot reach the Resume
/// aggregate to validate ownership (reference-by-id, CLAUDE.md §2.2). Owner-scoped
/// lookups mirror <c>DeleteResumeVersionCommandHandler</c>; a hit on an entity the
/// caller does not own is logged as a cross-user attempt and surfaced as NotFound.
/// </summary>
public sealed class AttachResumeVersionCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IFailedAccessLogger failedAccessLogger)
    : ICommandHandler<AttachResumeVersionCommand, Result>
{
    public async ValueTask<Result> Handle(
        AttachResumeVersionCommand command, CancellationToken cancellationToken)
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
            return Result.Failure(DomainError.NotFound("JobSeeker", currentUser.UserId.Value));

        var applicationId = new Jobbliggaren.Domain.Applications.ApplicationId(command.ApplicationId);
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
                    "Application", applicationId.Value, currentUser.UserId.Value, "AttachResumeVersion");
            }
            return Result.Failure(DomainError.NotFound("Application", command.ApplicationId));
        }

        // IDOR guard (BUILD §5.3): the version must belong to the caller's OWN
        // Resume. The ResumeVersion global query filter excludes soft-deleted
        // versions automatically.
        var versionId = new ResumeVersionId(command.ResumeVersionId);
        var ownsVersion = await db.Resumes
            .AsNoTracking()
            .Where(r => r.JobSeekerId == jobSeekerId)
            .SelectMany(r => r.Versions)
            .AnyAsync(v => v.Id == versionId, cancellationToken);

        if (!ownsVersion)
        {
            var versionExists = await db.Resumes
                .AsNoTracking()
                .SelectMany(r => r.Versions)
                .AnyAsync(v => v.Id == versionId, cancellationToken);
            if (versionExists)
            {
                failedAccessLogger.LogCrossUserAttempt(
                    "ResumeVersion", versionId.Value, currentUser.UserId.Value, "AttachResumeVersion");
            }
            return Result.Failure(DomainError.NotFound("ResumeVersion", command.ResumeVersionId));
        }

        return application.AttachResumeVersion(versionId, clock);
    }
}
