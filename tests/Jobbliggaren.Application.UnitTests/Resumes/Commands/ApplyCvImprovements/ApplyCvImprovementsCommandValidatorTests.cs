using Jobbliggaren.Application.Resumes.Commands.ApplyCvImprovements;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Commands.ApplyCvImprovements;

/// <summary>
/// Fas 4b PR-7 (#656) — input-shape validation for the frame-apply command. The bounded machine
/// tokens (criterion id, frame id, 64-hex fingerprint, slot map) are shape-checked at the boundary
/// so a malformed request never reaches the aggregate; the semantic grounding/verdict rules live in
/// the handler (FrameSlotGrounding / FromFrame), not here. SPEC-DRIVEN — RED until the command +
/// validator ship.
/// </summary>
public class ApplyCvImprovementsCommandValidatorTests
{
    private readonly ApplyCvImprovementsCommandValidator _validator = new();

    private static Dictionary<string, string> ValidSlots() =>
        new() { ["del1"] = "kundtjanst", ["del2"] = "support" };

    private static string ValidFingerprint => new('a', 64);

    private static FrameApplyInput ValidChange(
        string criterionId = "A2",
        string frameId = "sentence-ledde",
        IReadOnlyDictionary<string, string>? slots = null,
        string? fingerprint = null) =>
        new(criterionId, frameId, slots ?? ValidSlots(), fingerprint ?? ValidFingerprint);

    private static ApplyCvImprovementsCommand Command(
        Guid? id = null, IReadOnlyList<FrameApplyInput>? changes = null) =>
        new(id ?? Guid.NewGuid(), changes ?? [ValidChange()]);

    [Fact]
    public void Validate_ValidCommand_Passes()
    {
        _validator.Validate(Command()).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_EmptyResumeId_Fails()
    {
        var result = _validator.Validate(Command(id: Guid.Empty));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(ApplyCvImprovementsCommand.ResumeId));
    }

    [Fact]
    public void Validate_EmptyChanges_Fails()
    {
        _validator.Validate(Command(changes: [])).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_MoreThanTenChanges_Fails()
    {
        var changes = Enumerable.Range(0, 11).Select(_ => ValidChange()).ToList();

        _validator.Validate(Command(changes: changes)).IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData("A")]     // no digit
    [InlineData("A123")]  // three digits
    [InlineData("1A")]    // starts with a digit
    [InlineData("")]      // empty
    public void Validate_MalformedCriterionId_Fails(string criterionId)
    {
        var result = _validator.Validate(Command(changes: [ValidChange(criterionId: criterionId)]));

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_EmptyFrameId_Fails()
    {
        _validator.Validate(Command(changes: [ValidChange(frameId: "")])).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_FrameIdOverSixtyFourChars_Fails()
    {
        _validator.Validate(Command(changes: [ValidChange(frameId: new string('f', 65))]))
            .IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData("tooshort")]                                                    // not 64 chars
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // uppercase hex
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")] // non-hex
    public void Validate_MalformedFingerprint_Fails(string fingerprint)
    {
        var result = _validator.Validate(Command(changes: [ValidChange(fingerprint: fingerprint)]));

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_EmptySlotInputs_Fails()
    {
        _validator.Validate(Command(changes: [ValidChange(slots: new Dictionary<string, string>())]))
            .IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_MoreThanTwelveSlotInputs_Fails()
    {
        var slots = Enumerable.Range(0, 13).ToDictionary(i => $"k{i}", i => $"v{i}");

        _validator.Validate(Command(changes: [ValidChange(slots: slots)])).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_SlotKeyOverThirtyTwoChars_Fails()
    {
        var slots = new Dictionary<string, string> { [new string('k', 33)] = "v" };

        _validator.Validate(Command(changes: [ValidChange(slots: slots)])).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_SlotValueOverTwoHundredChars_Fails()
    {
        var slots = new Dictionary<string, string> { ["del1"] = new string('v', 201) };

        _validator.Validate(Command(changes: [ValidChange(slots: slots)])).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_SlotValueWithControlCharacter_Fails()
    {
        // U+0001 START OF HEADING — a control character has no place in user-echoed frame text.
        var value = "kundtjanst" + (char)1 + "support";
        var slots = new Dictionary<string, string> { ["del1"] = value };

        _validator.Validate(Command(changes: [ValidChange(slots: slots)])).IsValid.ShouldBeFalse();
    }
}
