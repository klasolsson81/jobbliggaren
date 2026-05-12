using FluentValidation;

namespace JobbPilot.Application.Waitlist.Commands.RequestWaitlistEntry;

public sealed class RequestWaitlistEntryCommandValidator : AbstractValidator<RequestWaitlistEntryCommand>
{
    public RequestWaitlistEntryCommandValidator()
    {
        RuleFor(c => c.Email).NotEmpty().EmailAddress().MaximumLength(254);
    }
}
