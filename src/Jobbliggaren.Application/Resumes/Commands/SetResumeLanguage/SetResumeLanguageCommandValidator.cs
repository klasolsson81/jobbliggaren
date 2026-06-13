using FluentValidation;

namespace Jobbliggaren.Application.Resumes.Commands.SetResumeLanguage;

public sealed class SetResumeLanguageCommandValidator : AbstractValidator<SetResumeLanguageCommand>
{
    public SetResumeLanguageCommandValidator()
    {
        RuleFor(c => c.ResumeId)
            .NotEqual(Guid.Empty).WithMessage("CV-id krävs.");

        RuleFor(c => c.Language)
            .NotEmpty().WithMessage("Språk krävs.")
            .Must(v => v is "Sv" or "En")
            .WithMessage("Språk måste vara Sv eller En.");
    }
}
