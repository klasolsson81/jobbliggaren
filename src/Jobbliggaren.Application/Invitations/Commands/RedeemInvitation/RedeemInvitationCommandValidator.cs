using FluentValidation;

namespace Jobbliggaren.Application.Invitations.Commands.RedeemInvitation;

public sealed class RedeemInvitationCommandValidator : AbstractValidator<RedeemInvitationCommand>
{
    public RedeemInvitationCommandValidator()
    {
        RuleFor(c => c.Token).NotEmpty().MinimumLength(20).MaximumLength(200);
        RuleFor(c => c.Password).NotEmpty().MinimumLength(8);
        RuleFor(c => c.DisplayName).NotEmpty().MaximumLength(200);
    }
}
