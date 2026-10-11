using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.JobSeekers.Commands.SetTopMatchMail;

/// <summary>
/// ADR 0164 (#2107) — loads the current user's JobSeeker tracked so <c>UnitOfWorkBehavior</c> persists the
/// change, and echoes its id for the audit row.
/// </summary>
public sealed class SetTopMatchMailCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : ICommandHandler<SetTopMatchMailCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(SetTopMatchMailCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return Result.Failure<Guid>(
                DomainError.Validation("JobSeeker.Unauthorized", "Användaren är inte autentiserad."));

        var jobSeeker = await db.JobSeekers
            .FirstOrDefaultAsync(js => js.UserId == currentUser.UserId.Value, cancellationToken);

        if (jobSeeker is null)
            return Result.Failure<Guid>(
                DomainError.NotFound("JobSeeker", currentUser.UserId.Value));

        jobSeeker.SetTopMatchMail(command.Enabled, clock);

        return Result.Success(jobSeeker.Id.Value);
    }
}
