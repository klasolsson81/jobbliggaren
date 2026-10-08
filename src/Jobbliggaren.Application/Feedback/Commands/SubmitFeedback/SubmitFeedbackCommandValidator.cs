using FluentValidation;

namespace Jobbliggaren.Application.Feedback.Commands.SubmitFeedback;

public sealed class SubmitFeedbackCommandValidator : AbstractValidator<SubmitFeedbackCommand>
{
    public SubmitFeedbackCommandValidator()
    {
        RuleFor(c => c.SubmissionKey).NotEmpty();
        RuleFor(c => c.PageKey).NotEmpty();
        RuleFor(c => c.Client).NotNull();
    }
}
