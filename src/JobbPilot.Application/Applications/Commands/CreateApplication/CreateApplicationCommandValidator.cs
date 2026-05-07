using FluentValidation;

namespace JobbPilot.Application.Applications.Commands.CreateApplication;

public sealed class CreateApplicationCommandValidator : AbstractValidator<CreateApplicationCommand>
{
    public CreateApplicationCommandValidator()
    {
        RuleFor(c => c.CoverLetter)
            .MaximumLength(10_000)
            .When(c => c.CoverLetter is not null)
            .WithMessage("Personligt brev får vara max 10 000 tecken.");
    }
}
