using FluentValidation;

namespace Jobbliggaren.Application.NotificationFeed.Commands.DismissNotification;

/// <summary>
/// What the request alone decides, before any read: a defined source and a real ad id. Whether the row exists, is the
/// caller's or is already gone is never an error here (the answer is the same 204).
/// </summary>
public sealed class DismissNotificationCommandValidator : AbstractValidator<DismissNotificationCommand>
{
    public DismissNotificationCommandValidator()
    {
        RuleFor(c => c.Kind).IsInEnum();
        RuleFor(c => c.JobAdId).NotEmpty();
    }
}
