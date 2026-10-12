using FluentValidation;

namespace Jobbliggaren.Application.NotificationFeed.Commands.MarkNotificationsReadThrough;

/// <summary>
/// What the request alone decides, before any read: a defined source and a token that is a real instant. The wire
/// refuses a missing or malformed token before this runs; the default instant is what an unbound value would be, and
/// it must never read as "through the beginning of time" by accident.
/// </summary>
public sealed class MarkNotificationsReadThroughCommandValidator : AbstractValidator<MarkNotificationsReadThroughCommand>
{
    public MarkNotificationsReadThroughCommandValidator()
    {
        RuleFor(c => c.Kind).IsInEnum();
        RuleFor(c => c.Through).NotEqual(default(DateTimeOffset));
    }
}
