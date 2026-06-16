using FluentValidation;

namespace Jobbliggaren.Application.Applications.Commands.AttachResumeVersion;

public sealed class AttachResumeVersionCommandValidator : AbstractValidator<AttachResumeVersionCommand>
{
    public AttachResumeVersionCommandValidator()
    {
        RuleFor(c => c.ApplicationId)
            .NotEmpty()
            .WithMessage("ApplicationId krävs.");

        RuleFor(c => c.ResumeVersionId)
            .NotEmpty()
            .WithMessage("ResumeVersionId krävs.");
    }
}
