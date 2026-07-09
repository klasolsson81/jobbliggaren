using FluentValidation;

namespace Jobbliggaren.Application.Resumes.Commands.DiscardParsedResume;

public sealed class DiscardParsedResumeCommandValidator
    : AbstractValidator<DiscardParsedResumeCommand>
{
    public DiscardParsedResumeCommandValidator()
    {
        RuleFor(c => c.ParsedResumeId).NotEmpty();
    }
}
