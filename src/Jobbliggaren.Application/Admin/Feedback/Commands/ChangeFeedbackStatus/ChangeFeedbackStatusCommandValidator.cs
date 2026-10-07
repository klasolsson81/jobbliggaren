using FluentValidation;

namespace Jobbliggaren.Application.Admin.Feedback.Commands.ChangeFeedbackStatus;

public sealed class ChangeFeedbackStatusCommandValidator : AbstractValidator<ChangeFeedbackStatusCommand>
{
    public ChangeFeedbackStatusCommandValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Status).IsInEnum();
    }
}
