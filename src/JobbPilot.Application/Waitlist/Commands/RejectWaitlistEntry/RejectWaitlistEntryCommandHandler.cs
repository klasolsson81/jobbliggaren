using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Domain.Common;
using JobbPilot.Domain.Waitlist;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace JobbPilot.Application.Waitlist.Commands.RejectWaitlistEntry;

public sealed class RejectWaitlistEntryCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : ICommandHandler<RejectWaitlistEntryCommand, Result>
{
    public async ValueTask<Result> Handle(
        RejectWaitlistEntryCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
            return Result.Failure(
                DomainError.Validation("Invitation.AdminUnknown", "Admin-användaren kunde inte identifieras."));

        var entryId = new WaitlistEntryId(command.WaitlistEntryId);
        var entry = await db.WaitlistEntries
            .FirstOrDefaultAsync(w => w.Id == entryId, cancellationToken);

        if (entry is null)
            return Result.Failure(
                DomainError.NotFound("WaitlistEntry", command.WaitlistEntryId));

        return entry.Reject(currentUser.UserId.Value, clock);
    }
}
