using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.Resumes.Common;
using Jobbliggaren.Application.Resumes.Queries;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Resumes;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Resumes.Commands.CreateResume;

public sealed class CreateResumeCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : ICommandHandler<CreateResumeCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(
        CreateResumeCommand command, CancellationToken cancellationToken)
    {
        // AuthorizationBehavior har redan kastat UnauthorizedException om
        // currentUser.IsAuthenticated == false (per ADR 0008 pipeline-ordning).
        if (!currentUser.UserId.HasValue)
            throw new UnauthorizedException();

        var jobSeekerId = await db.JobSeekers
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (jobSeekerId == default)
            return Result.Failure<Guid>(
                DomainError.NotFound("JobSeeker", currentUser.UserId.Value));

        // Fas 4b PR-4 (security-auditor Major, ADR 0074 Invariant 1): the template path
        // writes command.FullName into canonical Master content (ResumeContent.Empty), so
        // it is a canonical free-text write path and MUST run the same personnummer guard
        // as promote gap-fill (#499) and master edits — otherwise a personnummer typed
        // into the name field reaches an unflagged canonical CV, and the canonical B4
        // verdict ("checked on every save") would misreport (OQ3). Shared guard = SPOT:
        // same normalizer, scanner and error code as every other canonical write path.
        if (!string.IsNullOrWhiteSpace(command.FullName))
        {
            var guard = ResumeContentPersonnummerGuard.Check(new ResumeContentDto(
                new PersonalInfoDto(command.FullName, null, null, null), [], [], [], null));
            if (guard.IsFailure)
                return Result.Failure<Guid>(guard.Error);
        }

        var result = Resume.Create(jobSeekerId, command.Name, command.FullName, clock);
        if (result.IsFailure)
            return Result.Failure<Guid>(result.Error);

        db.Resumes.Add(result.Value);

        return Result.Success(result.Value.Id.Value);
    }
}
