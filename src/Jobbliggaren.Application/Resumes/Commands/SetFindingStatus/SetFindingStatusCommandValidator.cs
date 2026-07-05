using FluentValidation;
using Jobbliggaren.Domain.Resumes;

namespace Jobbliggaren.Application.Resumes.Commands.SetFindingStatus;

/// <summary>
/// Input-shape validation for the finding-status command (Fas 4b PR-4). The status must
/// parse fail-loud to a <see cref="ReviewFindingStatus"/> name (case-sensitive exact
/// match — a bad status is a client bug, never silently coerced); the criterion id's
/// shape is re-validated by the aggregate (<c>Resume.SetFindingStatus</c>), so this
/// layer only requires presence.
/// </summary>
public sealed class SetFindingStatusCommandValidator : AbstractValidator<SetFindingStatusCommand>
{
    public SetFindingStatusCommandValidator()
    {
        RuleFor(c => c.ResumeId)
            .NotEmpty().WithMessage("ResumeId krävs.");

        RuleFor(c => c.CriterionId)
            .NotEmpty().WithMessage("Kriterie-id krävs.");

        RuleFor(c => c.Status)
            .Must(s => ReviewFindingStatus.TryFromName(s, out _))
            .WithMessage("Status måste vara 'Open', 'Resolved' eller 'Ignored'.");
    }
}
