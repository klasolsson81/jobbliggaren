using FluentValidation;
using Jobbliggaren.Domain.Applications;

namespace Jobbliggaren.Application.Applications.Commands.TransitionTo;

public sealed class TransitionToCommandValidator : AbstractValidator<TransitionToCommand>
{
    public TransitionToCommandValidator()
    {
        RuleFor(c => c.ApplicationId)
            .NotEmpty()
            .WithMessage("ApplicationId är obligatoriskt.");

        RuleFor(c => c.TargetStatus)
            .NotEmpty()
            .WithMessage("TargetStatus är obligatoriskt.")
            .Must(s => ApplicationStatus.TryFromName(s, out _))
            .WithMessage("Okänd status.")
            .Must(s => s != ApplicationStatus.Ghosted.Name)
            .WithMessage("Ghosted-status sätts automatiskt av systemet.");
    }
}
