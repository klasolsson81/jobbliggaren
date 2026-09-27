using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.JobSeekers.Commands.UpdateMyProfile;

public sealed class UpdateMyProfileCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : ICommandHandler<UpdateMyProfileCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(
        UpdateMyProfileCommand command, CancellationToken cancellationToken)
    {
        var jobSeeker = await db.JobSeekers
            .FirstOrDefaultAsync(js => js.UserId == currentUser.UserId!.Value, cancellationToken)
            ?? throw new NotFoundException($"{nameof(JobSeeker)} hittades inte för användare {currentUser.UserId!.Value}.");

        if (command.Language is not null)
            jobSeeker.ChangeLanguage(command.Language, clock);

        // Echo the JobSeeker id for the audit row (AuditBehavior.ExtractAggregateId); the endpoint
        // discards the value and returns 200. Owner-scoped — no command-carried id.
        return Result.Success(jobSeeker.Id.Value);
    }
}
