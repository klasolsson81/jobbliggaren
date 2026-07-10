using FluentValidation;
using Jobbliggaren.Domain.Applications;

namespace Jobbliggaren.Application.Applications.Commands.RecordFollowUpOutcome;

public sealed class RecordFollowUpOutcomeCommandValidator
    : AbstractValidator<RecordFollowUpOutcomeCommand>
{
    // #644: recording an outcome resolves a Pending follow-up, so the only valid targets are the
    // two genuine resolutions. Pending (the initial state) and Logged (set solely at creation via
    // CreateLogged) are rejected, mirroring the domain guard in FollowUp.RecordOutcome.
    private static readonly string[] AllowedOutcomes =
        [FollowUpOutcome.Responded.Name, FollowUpOutcome.NoResponse.Name];

    public RecordFollowUpOutcomeCommandValidator()
    {
        RuleFor(c => c.ApplicationId).NotEmpty();

        RuleFor(c => c.FollowUpId).NotEmpty();

        RuleFor(c => c.Outcome)
            .NotEmpty()
            .Must(o => AllowedOutcomes.Contains(o))
            .WithMessage($"Utfall måste vara ett av: {string.Join(", ", AllowedOutcomes)}.");
    }
}
