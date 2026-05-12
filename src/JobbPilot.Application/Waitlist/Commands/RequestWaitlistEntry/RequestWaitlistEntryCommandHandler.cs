using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Waitlist.Dtos;
using JobbPilot.Domain.Common;
using JobbPilot.Domain.Waitlist;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace JobbPilot.Application.Waitlist.Commands.RequestWaitlistEntry;

public sealed class RequestWaitlistEntryCommandHandler(
    IAppDbContext db,
    IEmailSender emailSender,
    IDateTimeProvider clock)
    : ICommandHandler<RequestWaitlistEntryCommand, Result<WaitlistEntryRequestedDto>>
{
    public async ValueTask<Result<WaitlistEntryRequestedDto>> Handle(
        RequestWaitlistEntryCommand command, CancellationToken cancellationToken)
    {
        var normalizedEmail = command.Email!.Trim().ToLowerInvariant();

        // Idempotens: om email redan har en Pending-post, returnera den utan
        // dubblera. Partial unique index hindrar DB-dubblering men app-side
        // dedupe ger bättre UX (samma DTO tillbaka istället för 409-konflikt).
        var existing = await db.WaitlistEntries
            .FirstOrDefaultAsync(
                w => w.Email == normalizedEmail && w.Status == WaitlistStatus.Pending,
                cancellationToken);

        if (existing is not null)
            return Result.Success(WaitlistEntryRequestedDto.From(existing));

        var entryResult = WaitlistEntry.Request(command.Email, clock);
        if (entryResult.IsFailure)
            return Result.Failure<WaitlistEntryRequestedDto>(entryResult.Error);

        var entry = entryResult.Value;
        db.WaitlistEntries.Add(entry);

        await emailSender.SendWaitlistConfirmationAsync(entry.Email, cancellationToken);

        return Result.Success(WaitlistEntryRequestedDto.From(entry));
    }
}
