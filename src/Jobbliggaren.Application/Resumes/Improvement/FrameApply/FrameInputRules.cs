using FluentValidation;

namespace Jobbliggaren.Application.Resumes.Improvement.FrameApply;

/// <summary>
/// The ONE set of input-shape rules for frame inputs (Fas 4b PR-7, #656; architect review
/// Minor 1): shared by the apply command validator and the preview query validator so the
/// "a preview that succeeds is an apply that will succeed" contract holds at the shape
/// layer too — a Text slot is a free user echo semantically, but its transport shape
/// (length, control characters) is bounded identically on both surfaces.
/// </summary>
public static class FrameInputRules
{
    public static IRuleBuilderOptions<T, string> MustBeCriterionIdShape<T>(
        this IRuleBuilder<T, string> rule) =>
        // One uppercase letter + 1-2 digits — the rubric's criterion-id shape
        // (parity Resume.SetFindingStatus's IsValidCriterionId).
        rule.Matches("^[A-Z][0-9]{1,2}$")
            .WithMessage("Kriterie-id måste vara en bokstav följd av 1–2 siffror.");

    public static IRuleBuilderOptions<T, string> MustBeFrameIdShape<T>(
        this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("Ram-id krävs.")
            .MaximumLength(64).WithMessage("Ram-id får vara högst 64 tecken.");

    public static IRuleBuilderOptions<T, IReadOnlyDictionary<string, string>> MustBeSlotInputsShape<T>(
        this IRuleBuilder<T, IReadOnlyDictionary<string, string>> rule) =>
        rule.NotEmpty().WithMessage("Ram-fälten krävs.")
            .Must(s => s.Count <= 12).WithMessage("Högst tolv ram-fält.")
            .Must(s => s.Keys.All(k => !string.IsNullOrWhiteSpace(k) && k.Length <= 32))
            .WithMessage("Ram-fältens namn får vara högst 32 tecken.")
            .Must(s => s.Values.All(v => v is { Length: <= 200 }))
            .WithMessage("Ram-fältens värden får vara högst 200 tecken.")
            .Must(s => s.Values.All(v => v is null || !v.Any(char.IsControl)))
            .WithMessage("Ram-fältens värden får inte innehålla kontrolltecken.")
            // Security review Minor 2: a brace in a slot value would survive substitution
            // and trip the factory's residual-placeholder guard as a 500 — reject it here
            // as the friendly 400 instead (the template's {slot} syntax is reserved).
            .Must(s => s.Values.All(v => v is null || (!v.Contains('{') && !v.Contains('}'))))
            .WithMessage("Ram-fältens värden får inte innehålla klammerparenteser.");
}
