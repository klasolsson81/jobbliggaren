using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.Resumes.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Resumes;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Resumes.Commands.UpdateMasterContent;

public sealed class UpdateMasterContentCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IFailedAccessLogger failedAccessLogger)
    : ICommandHandler<UpdateMasterContentCommand, Result>
{
    public async ValueTask<Result> Handle(
        UpdateMasterContentCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            throw new UnauthorizedException();

        var jobSeekerId = await db.JobSeekers
            .AsNoTracking()
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var resumeId = new ResumeId(command.ResumeId);
        // FindingStatuses is part of the write-path Include contract (Fas 4b PR-4,
        // CTO-bind Q2): UpdateMasterContent stamps staleness on previously-resolved
        // findings IN the same transaction — an unloaded collection would silently
        // skip the stamp.
        var resume = await db.Resumes
            .Include(r => r.Versions)
            .Include(r => r.FindingStatuses)
            .FirstOrDefaultAsync(r => r.Id == resumeId && r.JobSeekerId == jobSeekerId, cancellationToken);

        if (resume is null)
        {
            var exists = await db.Resumes
                .AsNoTracking()
                .AnyAsync(r => r.Id == resumeId, cancellationToken);
            if (exists)
            {
                failedAccessLogger.LogCrossUserAttempt(
                    "Resume", resumeId.Value, currentUser.UserId.Value, "UpdateMasterContent");
            }
            throw new NotFoundException("CV hittades inte.");
        }

        // #499 (ADR 0074 Invariant 1): re-run the personnummer guard on the user-submitted
        // content BEFORE it becomes canonical Resume Master content. UpdateMasterContent runs
        // only structural ValidateContent, so without this a personnummer typed into the master
        // edit would reach an unflagged canonical Resume (render/PDF). Same shared guard as
        // promote (highest-priority PII first — before ToDomain/ValidateContent). Owner already
        // resolved above, so this never runs for a cross-user request.
        var guard = ResumeContentPersonnummerGuard.Check(command.Content);
        if (guard.IsFailure)
            return guard;

        var content = ResumeContentMapper.ToDomain(command.Content);
        return resume.UpdateMasterContent(content, clock);
    }
}
