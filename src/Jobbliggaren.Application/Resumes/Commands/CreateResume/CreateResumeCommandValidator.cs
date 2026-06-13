using FluentValidation;

namespace Jobbliggaren.Application.Resumes.Commands.CreateResume;

public sealed class CreateResumeCommandValidator : AbstractValidator<CreateResumeCommand>
{
    public CreateResumeCommandValidator()
    {
        RuleFor(c => c.Name)
            .NotEmpty().WithMessage("Namn på CV är obligatoriskt.")
            .MaximumLength(200).WithMessage("Namn får vara max 200 tecken.");

        RuleFor(c => c.FullName)
            .NotEmpty().WithMessage("Fullständigt namn krävs.")
            .MaximumLength(200).WithMessage("Fullständigt namn får vara max 200 tecken.");
    }
}
