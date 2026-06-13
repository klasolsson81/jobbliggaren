using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Invitations.Dtos;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Invitations;
using Mediator;

namespace Jobbliggaren.Application.Invitations.Commands.IssueInvitation;

public sealed class IssueInvitationCommandHandler(
    IAppDbContext db,
    IInvitationTokenGenerator tokenGenerator,
    IEmailSender emailSender,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : ICommandHandler<IssueInvitationCommand, Result<InvitationIssuedDto>>
{
    private const int DefaultValidForDays = 7;

    public async ValueTask<Result<InvitationIssuedDto>> Handle(
        IssueInvitationCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
            return Result.Failure<InvitationIssuedDto>(
                DomainError.Validation("Invitation.AdminUnknown", "Admin-användaren kunde inte identifieras."));

        var validFor = TimeSpan.FromDays(command.ValidForDays ?? DefaultValidForDays);
        var token = tokenGenerator.Generate();

        var issueResult = Invitation.Issue(
            command.Email,
            InvitationOrigin.DirectInvite,
            token.Hash,
            validFor,
            currentUser.UserId.Value,
            clock);

        if (issueResult.IsFailure)
            return Result.Failure<InvitationIssuedDto>(issueResult.Error);

        var invitation = issueResult.Value;
        db.Invitations.Add(invitation);

        // Email skickas i samma flöde — om SES misslyckas rullas hela
        // UoW tillbaka och Invitation persisteras inte (cleaner än att
        // ha "ghost"-poster utan email-leverans). SES-fel bubblar som
        // exception och fångas av middleware.
        await emailSender.SendInvitationEmailAsync(
            invitation.Email,
            token.Plaintext,
            invitation.ExpiresAt,
            cancellationToken);

        return Result.Success(InvitationIssuedDto.From(invitation));
    }
}
