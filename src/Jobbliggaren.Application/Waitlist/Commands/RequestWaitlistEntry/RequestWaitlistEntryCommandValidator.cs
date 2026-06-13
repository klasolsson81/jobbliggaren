using FluentValidation;
using Jobbliggaren.Domain.Waitlist;

namespace Jobbliggaren.Application.Waitlist.Commands.RequestWaitlistEntry;

public sealed class RequestWaitlistEntryCommandValidator
    : AbstractValidator<RequestWaitlistEntryCommand>
{
    public RequestWaitlistEntryCommandValidator()
    {
        RuleFor(c => c.Email)
            .NotEmpty().WithMessage("E-postadress krävs.")
            .EmailAddress().WithMessage("E-postadressen är inte giltig.")
            .MaximumLength(254).WithMessage("E-postadress får vara max 254 tecken.");

        RuleFor(c => c.Name)
            .NotEmpty().WithMessage("Namn krävs.")
            .Length(WaitlistEntry.NameMinLength, WaitlistEntry.NameMaxLength)
            .WithMessage($"Namn ska vara {WaitlistEntry.NameMinLength}–{WaitlistEntry.NameMaxLength} tecken.");

        RuleFor(c => c.Motivation)
            .NotEmpty().WithMessage("Motivering krävs.")
            .Length(WaitlistEntry.MotivationMinLength, WaitlistEntry.MotivationMaxLength)
            .WithMessage($"Motivering ska vara {WaitlistEntry.MotivationMinLength}–{WaitlistEntry.MotivationMaxLength} tecken.");

        // MarketingEmailAccepted är boolean opt-in — ingen validering
        // (både true och false är giltiga; default är false).
    }
}
