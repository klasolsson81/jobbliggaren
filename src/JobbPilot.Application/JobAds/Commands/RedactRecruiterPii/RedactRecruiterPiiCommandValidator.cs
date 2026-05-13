using FluentValidation;

namespace JobbPilot.Application.JobAds.Commands.RedactRecruiterPii;

public sealed class RedactRecruiterPiiCommandValidator : AbstractValidator<RedactRecruiterPiiCommand>
{
    public RedactRecruiterPiiCommandValidator()
    {
        RuleFor(c => c.Identifier)
            .NotEmpty()
            .WithMessage("Identifier krävs.")
            .MaximumLength(254)
            .WithMessage("Identifier får vara max 254 tecken.");

        // Email-format-validering bara när Type=Email — Name-branch validerar inte
        // (irrelevant i Fas 2 eftersom handler returnerar NameNotSupportedYet).
        RuleFor(c => c.Identifier)
            .EmailAddress()
            .When(c => c.Type == RecruiterIdentifierType.Email)
            .WithMessage("Identifier måste vara en giltig e-postadress när Type=Email.");

        RuleFor(c => c.Type).IsInEnum();
    }
}
