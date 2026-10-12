using FluentValidation;

namespace Jobbliggaren.Application.NotificationFeed.Commands.MarkNotificationFeedBacklogRead;

public sealed class MarkNotificationFeedBacklogReadCommandValidator
    : AbstractValidator<MarkNotificationFeedBacklogReadCommand>
{
    public MarkNotificationFeedBacklogReadCommandValidator()
    {
        RuleFor(c => c.Kind).IsInEnum();
    }
}
