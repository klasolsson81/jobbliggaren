using FluentValidation;

namespace Jobbliggaren.Application.Waitlist.Commands.ApproveWaitlistEntry;

public sealed class ApproveWaitlistEntryCommandValidator : AbstractValidator<ApproveWaitlistEntryCommand>
{
    public ApproveWaitlistEntryCommandValidator()
    {
        RuleFor(c => c.WaitlistEntryId).NotEqual(Guid.Empty);
        RuleFor(c => c.ValidForDays)
            .GreaterThan(0)
            .LessThanOrEqualTo(30)
            .When(c => c.ValidForDays.HasValue);
    }
}
