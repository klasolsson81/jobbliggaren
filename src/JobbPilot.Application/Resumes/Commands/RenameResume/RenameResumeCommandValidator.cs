using FluentValidation;

namespace JobbPilot.Application.Resumes.Commands.RenameResume;

public sealed class RenameResumeCommandValidator : AbstractValidator<RenameResumeCommand>
{
    public RenameResumeCommandValidator()
    {
        RuleFor(c => c.Name)
            .NotEmpty().WithMessage("Namn på CV är obligatoriskt.")
            .MaximumLength(200).WithMessage("Namn får vara max 200 tecken.");
    }
}
