using FluentValidation;

namespace Jobbliggaren.Application.Applications.Commands.LogFollowUp;

public sealed class LogFollowUpCommandValidator : AbstractValidator<LogFollowUpCommand>
{
    public LogFollowUpCommandValidator()
    {
        RuleFor(c => c.ApplicationId).NotEmpty();

        RuleFor(c => c.Note)
            .MaximumLength(2000)
            .When(c => c.Note is not null);
    }
}
