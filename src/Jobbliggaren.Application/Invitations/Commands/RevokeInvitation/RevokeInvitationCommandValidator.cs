using FluentValidation;

namespace Jobbliggaren.Application.Invitations.Commands.RevokeInvitation;

public sealed class RevokeInvitationCommandValidator : AbstractValidator<RevokeInvitationCommand>
{
    public RevokeInvitationCommandValidator()
    {
        RuleFor(c => c.InvitationId).NotEqual(Guid.Empty);
    }
}
