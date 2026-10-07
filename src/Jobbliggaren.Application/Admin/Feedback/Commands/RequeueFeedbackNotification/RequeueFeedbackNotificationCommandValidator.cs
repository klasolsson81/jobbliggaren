using FluentValidation;

namespace Jobbliggaren.Application.Admin.Feedback.Commands.RequeueFeedbackNotification;

public sealed class RequeueFeedbackNotificationCommandValidator : AbstractValidator<RequeueFeedbackNotificationCommand>
{
    public RequeueFeedbackNotificationCommandValidator() => RuleFor(c => c.Id).NotEmpty();
}
