using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Waitlist.Dtos;
using JobbPilot.Domain.Common;
using JobbPilot.Domain.Waitlist;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace JobbPilot.Application.Waitlist.Commands.RequestWaitlistEntry;

public sealed class RequestWaitlistEntryCommandHandler(
    IAppDbContext db,
    IEmailSender emailSender,
    IDateTimeProvider clock,
    IOptions<PrivacyPolicyOptions> privacyPolicyOptions)
    : ICommandHandler<RequestWaitlistEntryCommand, Result<WaitlistEntryRequestedDto>>
{
    public async ValueTask<Result<WaitlistEntryRequestedDto>> Handle(
        RequestWaitlistEntryCommand command, CancellationToken cancellationToken)
    {
        var normalizedEmail = (command.Email ?? string.Empty).Trim().ToLowerInvariant();
        var now = clock.UtcNow;
        var acceptance = new AcceptanceSnapshot(
            MarketingEmailAccepted: command.MarketingEmailAccepted,
            AcceptedAt: now,
            PrivacyPolicyVersion: privacyPolicyOptions.Value.CurrentVersion);

        // Idempotent re-signup (GDPR Art. 7(3)): om email redan har en Pending-post,
        // refresh:a den med nya värden + bevara RequestedAt (FIFO-position).
        // Partial unique index hindrar DB-dubblering men app-side dedupe + refresh
        // ger bättre UX (uppdaterad acceptance-snapshot returneras + nytt bekräftelsemail
        // skickas inte vid re-signup).
        var existing = await db.WaitlistEntries
            .FirstOrDefaultAsync(
                w => w.Email == normalizedEmail && w.Status == WaitlistStatus.Pending,
                cancellationToken);

        if (existing is not null)
        {
            var refreshResult = existing.RefreshRequest(
                command.Name, command.Motivation, acceptance, clock);
            if (refreshResult.IsFailure)
                return Result.Failure<WaitlistEntryRequestedDto>(refreshResult.Error);

            return Result.Success(WaitlistEntryRequestedDto.From(existing));
        }

        var entryResult = WaitlistEntry.Request(
            command.Email, command.Name, command.Motivation, acceptance, clock);
        if (entryResult.IsFailure)
            return Result.Failure<WaitlistEntryRequestedDto>(entryResult.Error);

        var entry = entryResult.Value;
        db.WaitlistEntries.Add(entry);

        await emailSender.SendWaitlistConfirmationAsync(entry.Email, cancellationToken);

        return Result.Success(WaitlistEntryRequestedDto.From(entry));
    }
}
