using FluentValidation;

namespace Jobbliggaren.Application.NotificationFeed.Commands.MarkNotificationRead;

/// <summary>
/// What the request alone decides, before any read: a defined source and a real ad id. Whether the row exists, is the
/// caller's or is still listed is never an error here (the answer is the same 204).
/// </summary>
public sealed class MarkNotificationReadCommandValidator : AbstractValidator<MarkNotificationReadCommand>
{
    public MarkNotificationReadCommandValidator()
    {
        RuleFor(c => c.Kind).IsInEnum();
        RuleFor(c => c.JobAdId).NotEmpty();
    }
}
