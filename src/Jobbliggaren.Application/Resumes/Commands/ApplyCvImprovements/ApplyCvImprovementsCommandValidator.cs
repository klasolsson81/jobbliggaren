using FluentValidation;

namespace Jobbliggaren.Application.Resumes.Commands.ApplyCvImprovements;

/// <summary>
/// Input-shape validation for the frame-apply command (Fas 4b PR-7, #656). Bounded
/// machine tokens only — the semantic rules (frame existence, criterion match, slot
/// grounding, fingerprint equality) live in the handler/composer where the live review
/// is available; this layer rejects malformed shapes before any work is done.
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
            // One uppercase letter + 1-2 digits — the rubric's criterion-id shape
            // (parity Resume.SetFindingStatus's IsValidCriterionId).
            change.RuleFor(x => x.CriterionId)
                .Matches("^[A-Z][0-9]{1,2}$")
                .WithMessage("Kriterie-id måste vara en bokstav följd av 1–2 siffror.");

            change.RuleFor(x => x.FrameId)
                .NotEmpty().WithMessage("Ram-id krävs.")
                .MaximumLength(64).WithMessage("Ram-id får vara högst 64 tecken.");

            // Full SHA-256 as lowercase hex (parity the ledger's fingerprint shape).
            change.RuleFor(x => x.FindingFingerprint)
                .Matches("^[0-9a-f]{64}$")
                .WithMessage("Fingeravtrycket måste vara 64 hexadecimala tecken.");

            change.RuleFor(x => x.SlotInputs)
                .NotEmpty().WithMessage("Ram-fälten krävs.")
                .Must(s => s.Count <= 12).WithMessage("Högst tolv ram-fält.")
                .Must(s => s.Keys.All(k => !string.IsNullOrWhiteSpace(k) && k.Length <= 32))
                .WithMessage("Ram-fältens namn får vara högst 32 tecken.")
                .Must(s => s.Values.All(v => v is { Length: <= 200 }))
                .WithMessage("Ram-fältens värden får vara högst 200 tecken.")
                .Must(s => s.Values.All(v => v is null || !v.Any(char.IsControl)))
                .WithMessage("Ram-fältens värden får inte innehålla kontrolltecken.");
        });
    }
}
