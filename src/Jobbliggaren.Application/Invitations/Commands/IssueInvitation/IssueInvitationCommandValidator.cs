using FluentValidation;

namespace Jobbliggaren.Application.Invitations.Commands.IssueInvitation;

public sealed class IssueInvitationCommandValidator : AbstractValidator<IssueInvitationCommand>
{
    public IssueInvitationCommandValidator()
    {
        RuleFor(c => c.Email).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(c => c.ValidForDays)
            .GreaterThan(0)
            .LessThanOrEqualTo(30)
            .When(c => c.ValidForDays.HasValue);
    }
}
