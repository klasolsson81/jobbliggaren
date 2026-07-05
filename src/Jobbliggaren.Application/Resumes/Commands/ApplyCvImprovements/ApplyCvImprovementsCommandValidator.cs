using FluentValidation;
using Jobbliggaren.Application.Resumes.Improvement.FrameApply;

namespace Jobbliggaren.Application.Resumes.Commands.ApplyCvImprovements;

/// <summary>
/// Input-shape validation for the frame-apply command (Fas 4b PR-7, #656). Bounded
/// machine tokens only — the semantic rules (frame existence, criterion match, slot
/// grounding, fingerprint equality) live in the handler/composer where the live review
/// is available; this layer rejects malformed shapes before any work is done. The
/// per-change shape rules are the SHARED <see cref="FrameInputRules"/> (one rule source
/// with the preview validator — architect review Minor 1); only the fingerprint echo is
/// apply-specific (the preview MINTS it, so it has none to validate).
/// </summary>
public sealed class ApplyCvImprovementsCommandValidator : AbstractValidator<ApplyCvImprovementsCommand>
{
    public ApplyCvImprovementsCommandValidator()
    {
        RuleFor(c => c.ResumeId)
            .NotEmpty().WithMessage("ResumeId krävs.");

        RuleFor(c => c.Changes)
            .NotEmpty().WithMessage("Minst en ändring krävs.")
            .Must(changes => changes.Count <= 10)
            .WithMessage("Högst tio ändringar per anrop.");

        RuleForEach(c => c.Changes).ChildRules(change =>
        {
            change.RuleFor(x => x.CriterionId).MustBeCriterionIdShape();
            change.RuleFor(x => x.FrameId).MustBeFrameIdShape();
            change.RuleFor(x => x.SlotInputs).MustBeSlotInputsShape();

            // Full SHA-256 as lowercase hex (parity the ledger's fingerprint shape).
            change.RuleFor(x => x.FindingFingerprint)
                .Matches("^[0-9a-f]{64}$")
                .WithMessage("Fingeravtrycket måste vara 64 hexadecimala tecken.");
        });
    }
}
