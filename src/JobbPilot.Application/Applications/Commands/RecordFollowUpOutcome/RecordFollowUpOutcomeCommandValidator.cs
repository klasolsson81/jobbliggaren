using FluentValidation;
using JobbPilot.Domain.Applications;

namespace JobbPilot.Application.Applications.Commands.RecordFollowUpOutcome;

public sealed class RecordFollowUpOutcomeCommandValidator
    : AbstractValidator<RecordFollowUpOutcomeCommand>
{
    public RecordFollowUpOutcomeCommandValidator()
    {
        RuleFor(c => c.ApplicationId).NotEmpty();

        RuleFor(c => c.FollowUpId).NotEmpty();

        RuleFor(c => c.Outcome)
            .NotEmpty()
            .Must(o => FollowUpOutcome.TryFromName(o, out _))
            .WithMessage($"Utfall måste vara ett av: {string.Join(", ", FollowUpOutcome.List.Select(x => x.Name))}.");
    }
}
