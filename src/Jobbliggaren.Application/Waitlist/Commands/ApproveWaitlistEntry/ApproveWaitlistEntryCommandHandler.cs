using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Invitations.Dtos;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Invitations;
using Jobbliggaren.Domain.Waitlist;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Waitlist.Commands.ApproveWaitlistEntry;

public sealed class ApproveWaitlistEntryCommandHandler(
    IAppDbContext db,
    IInvitationTokenGenerator tokenGenerator,
    IEmailSender emailSender,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : ICommandHandler<ApproveWaitlistEntryCommand, Result<InvitationIssuedDto>>
{
    private const int DefaultValidForDays = 7;

    public async ValueTask<Result<InvitationIssuedDto>> Handle(
        ApproveWaitlistEntryCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
            return Result.Failure<InvitationIssuedDto>(
                DomainError.Validation("Invitation.AdminUnknown", "Admin-användaren kunde inte identifieras."));

        var entryId = new WaitlistEntryId(command.WaitlistEntryId);
        var entry = await db.WaitlistEntries
            .FirstOrDefaultAsync(w => w.Id == entryId, cancellationToken);

        if (entry is null)
            return Result.Failure<InvitationIssuedDto>(
                DomainError.NotFound("WaitlistEntry", command.WaitlistEntryId));

        var validFor = TimeSpan.FromDays(command.ValidForDays ?? DefaultValidForDays);
        var token = tokenGenerator.Generate();

        var issueResult = Invitation.Issue(
            entry.Email,
            InvitationOrigin.WaitlistApproved,
            token.Hash,
            validFor,
            currentUser.UserId.Value,
            clock);

        if (issueResult.IsFailure)
            return Result.Failure<InvitationIssuedDto>(issueResult.Error);

        var invitation = issueResult.Value;

        // WaitlistEntry.Approve länkar mot invitation atomically via samma UoW.
        var approveResult = entry.Approve(currentUser.UserId.Value, invitation.Id, clock);
        if (approveResult.IsFailure)
            return Result.Failure<InvitationIssuedDto>(approveResult.Error);

        db.Invitations.Add(invitation);

        await emailSender.SendInvitationEmailAsync(
            invitation.Email,
            token.Plaintext,
            invitation.ExpiresAt,
            cancellationToken);

        return Result.Success(InvitationIssuedDto.From(invitation));
    }
}
