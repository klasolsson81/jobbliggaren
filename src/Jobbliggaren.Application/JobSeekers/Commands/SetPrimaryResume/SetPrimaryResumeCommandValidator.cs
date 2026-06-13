using FluentValidation;

namespace Jobbliggaren.Application.JobSeekers.Commands.SetPrimaryResume;

public sealed class SetPrimaryResumeCommandValidator : AbstractValidator<SetPrimaryResumeCommand>
{
    public SetPrimaryResumeCommandValidator()
    {
        RuleFor(c => c.ResumeId)
            .NotEqual(Guid.Empty).WithMessage("CV-id krävs.");
    }
}
