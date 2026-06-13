using FluentValidation;
using Jobbliggaren.Domain.Applications;

namespace Jobbliggaren.Application.Applications.Commands.AddFollowUp;

public sealed class AddFollowUpCommandValidator : AbstractValidator<AddFollowUpCommand>
{
    public AddFollowUpCommandValidator()
    {
        RuleFor(c => c.ApplicationId).NotEmpty();

        RuleFor(c => c.Channel)
            .NotEmpty()
            .Must(c => FollowUpChannel.TryFromName(c, out _))
            .WithMessage($"Kanal måste vara en av: {string.Join(", ", FollowUpChannel.List.Select(x => x.Name))}.");

        RuleFor(c => c.ScheduledAt).NotEmpty();

        RuleFor(c => c.Note)
            .MaximumLength(2000)
            .When(c => c.Note is not null);
    }
}
