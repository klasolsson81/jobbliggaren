using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Exceptions;
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

        var result = Resume.Create(jobSeekerId, command.Name, command.FullName, clock);
        if (result.IsFailure)
            return Result.Failure<Guid>(result.Error);

        db.Resumes.Add(result.Value);

        return Result.Success(result.Value.Id.Value);
    }
}
