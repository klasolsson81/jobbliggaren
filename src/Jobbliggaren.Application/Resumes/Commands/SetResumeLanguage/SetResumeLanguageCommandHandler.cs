using Ardalis.SmartEnum;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Resumes;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Resumes.Commands.SetResumeLanguage;

public sealed class SetResumeLanguageCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IFailedAccessLogger failedAccessLogger)
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
        var resume = await db.Resumes
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

        return resume.SetLanguage(lang, clock);
    }
}
