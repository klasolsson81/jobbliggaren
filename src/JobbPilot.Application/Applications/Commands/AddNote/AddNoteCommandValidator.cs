using FluentValidation;

namespace JobbPilot.Application.Applications.Commands.AddNote;

public sealed class AddNoteCommandValidator : AbstractValidator<AddNoteCommand>
{
    public AddNoteCommandValidator()
    {
        RuleFor(c => c.ApplicationId).NotEmpty();

        RuleFor(c => c.Content)
            .NotEmpty()
            .WithMessage("Innehåll är obligatoriskt.")
            .MaximumLength(5000)
            .WithMessage("Anteckning får vara max 5 000 tecken.");
    }
}
