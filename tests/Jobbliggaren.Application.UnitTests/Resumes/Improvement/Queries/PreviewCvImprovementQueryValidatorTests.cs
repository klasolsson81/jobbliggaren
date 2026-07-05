using FluentValidation.TestHelper;
using Jobbliggaren.Application.Resumes.Improvement.Queries.PreviewCvImprovement;
using Jobbliggaren.Application.UnitTests.Resumes.Improvement;

namespace Jobbliggaren.Application.UnitTests.Resumes.Improvement.Queries;

/// <summary>
/// Fas 4b PR-7 (#656, architect review Minor 1) — the preview query's input-shape gate:
/// the SAME shared <c>FrameInputRules</c> the apply command enforces, so the preview↔apply
/// "same gates" contract holds at the shape layer (a free-echo Text slot is semantically
/// unconstrained but its transport shape is bounded identically on both surfaces).
/// </summary>
public class PreviewCvImprovementQueryValidatorTests
{
    private readonly PreviewCvImprovementQueryValidator _validator = new();

    private static PreviewCvImprovementQuery Query(
        Guid? resumeId = null,
        string criterionId = "A2",
        string frameId = "sentence-ledde",
        IReadOnlyDictionary<string, string>? slots = null) =>
        new(resumeId ?? Guid.NewGuid(), criterionId, frameId, slots ?? FrameFixtures.LeddeSlots());

    [Fact]
    public void Validate_ValidQuery_Passes()
    {
        _validator.TestValidate(Query()).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyResumeId_Fails()
    {
        _validator.TestValidate(Query(resumeId: Guid.Empty))
            .ShouldHaveValidationErrorFor(q => q.ResumeId);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("A123")]
    [InlineData("1A")]
    [InlineData("")]
    public void Validate_MalformedCriterionId_Fails(string criterionId)
    {
        _validator.TestValidate(Query(criterionId: criterionId))
            .ShouldHaveValidationErrorFor(q => q.CriterionId);
    }

    [Fact]
    public void Validate_FrameIdOverSixtyFourChars_Fails()
    {
        _validator.TestValidate(Query(frameId: new string('x', 65)))
            .ShouldHaveValidationErrorFor(q => q.FrameId);
    }

    [Fact]
    public void Validate_SlotValueOverTwoHundredChars_Fails()
    {
        var slots = FrameFixtures.LeddeSlots();
        slots["del1"] = new string('x', 201);

        _validator.TestValidate(Query(slots: slots))
            .ShouldHaveValidationErrorFor(q => q.SlotInputs);
    }

    [Fact]
    public void Validate_SlotValueWithControlCharacter_Fails()
    {
        var slots = FrameFixtures.LeddeSlots();
        slots["del1"] = "kundtjänst" + (char)1;

        _validator.TestValidate(Query(slots: slots))
            .ShouldHaveValidationErrorFor(q => q.SlotInputs);
    }

    [Theory]
    [InlineData("kund{tjänst")]
    [InlineData("kund}tjänst")]
    public void Validate_SlotValueWithBrace_Fails(string value)
    {
        // Security review Minor 2: a brace would survive substitution and trip the
        // factory's residual-placeholder guard as a 500 — the validator must 400 it.
        var slots = FrameFixtures.LeddeSlots();
        slots["del1"] = value;

        _validator.TestValidate(Query(slots: slots))
            .ShouldHaveValidationErrorFor(q => q.SlotInputs);
    }

    [Fact]
    public void Validate_MoreThanTwelveSlotInputs_Fails()
    {
        var slots = new Dictionary<string, string>();
        for (var i = 0; i < 13; i++)
        {
            slots[$"s{i}"] = "v";
        }

        _validator.TestValidate(Query(slots: slots))
            .ShouldHaveValidationErrorFor(q => q.SlotInputs);
    }
}
