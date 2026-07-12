using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Resumes;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Resumes.Commands.ChangeTemplateOptions;

public sealed class ChangeTemplateOptionsCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IFailedAccessLogger failedAccessLogger)
    : ICommandHandler<ChangeTemplateOptionsCommand, Result>
{
    public async ValueTask<Result> Handle(
        ChangeTemplateOptionsCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            throw new UnauthorizedException();

        var jobSeekerId = await db.JobSeekers
            .AsNoTracking()
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var resumeId = new ResumeId(command.ResumeId);
        // Root only: the owned TemplateOptions VO materializes with the tracked root
        // (required owned navigation) — no Include needed; Versions/findings are untouched
        // (a template change is not a review input, bind Q4). Mirrors RenameResume.
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
                    "Resume", resumeId.Value, currentUser.UserId.Value, "ChangeTemplateOptions");
            }
            throw new NotFoundException("CV hittades inte.");
        }

        // Defense-in-depth: the validator already resolved these names, but a direct
        // Send (test / future caller) must degrade to a 400, never an unmapped throw.
        if (!CvTemplate.TryFromName(command.Template, out var template)
            || !CvAccentColor.TryFromName(command.AccentColor, out var accent)
            || !CvFontPair.TryFromName(command.FontPair, out var fontPair)
            || !CvDensity.TryFromName(command.Density, out var density))
        {
            return Result.Failure(DomainError.Validation(
                "Resume.TemplateOptionsInvalid", "Ogiltiga mallinställningar."));
        }

        // Preserve the persisted photo config: the photo feature is DPIA-gated to PR-10, so
        // the write-path never enables a photo (fail-closed by construction). TemplateOptions
        // is non-nullable (required owned navigation, always at least Default).
        var current = resume.TemplateOptions;
        var options = new CvTemplateOptions(
            template, accent, fontPair, density, current.PhotoEnabled, current.PhotoShape);

        return resume.ChangeTemplateOptions(options, clock);
    }
}
