using FluentValidation;

namespace Jobbliggaren.Application.Waitlist.Commands.RejectWaitlistEntry;

public sealed class RejectWaitlistEntryCommandValidator : AbstractValidator<RejectWaitlistEntryCommand>
{
    public RejectWaitlistEntryCommandValidator()
    {
        RuleFor(c => c.WaitlistEntryId).NotEqual(Guid.Empty);
    }
}
