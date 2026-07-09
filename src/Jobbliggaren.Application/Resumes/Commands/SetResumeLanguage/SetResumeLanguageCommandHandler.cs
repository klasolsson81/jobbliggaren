using Ardalis.SmartEnum;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Resumes;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Resumes.Commands.SetResumeLanguage;

public sealed class SetResumeLanguageCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IFailedAccessLogger failedAccessLogger,
    IResumeReviewReconciler reconciler)
    : ICommandHandler<SetResumeLanguageCommand, Result>
{
    public async ValueTask<Result> Handle(
        SetResumeLanguageCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            throw new UnauthorizedException();

        var jobSeekerId = await db.JobSeekers
            .AsNoTracking()
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var resumeId = new ResumeId(command.ResumeId);
        // Versions + FindingStatuses ride the write-path Include contract (Fas 4b PR-8):
        // the language drives the review (e.g. C7's dictionary), so this write reconciles
        // the ledger and needs the master content + the finding rows loaded.
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
                    "Resume", resumeId.Value, currentUser.UserId.Value, "SetResumeLanguage");
            }
            throw new NotFoundException("CV hittades inte.");
        }

        if (!SmartEnum<ResumeLanguage>.TryFromName(command.Language, out var lang))
            return Result.Failure(DomainError.Validation(
                "Resume.LanguageInvalid", $"Okänt språk: {command.Language}."));

        var set = resume.SetLanguage(lang, clock);
        if (set.IsFailure)
            return set;

        // Fas 4b PR-8 (CTO-bind Q1): the language changes what the engine assesses, so
        // the ledger reconciles here like every other review-input write.
        return await reconciler.ReconcileAsync(resume, null, cancellationToken);
    }
}
