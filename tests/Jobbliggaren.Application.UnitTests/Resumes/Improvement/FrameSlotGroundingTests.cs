using Jobbliggaren.Application.Resumes.Improvement.Abstractions;
using Jobbliggaren.Domain.Common;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Improvement;

/// <summary>
/// Fas 4b PR-7 (#656, ADR 0093 §D2) — the shared, Result-returning rule core the apply/preview
/// handlers call: the SAME three invariants <see cref="ProposedChange.FromFrame"/> throws on, but
/// surfaced as typed <see cref="DomainError"/> failures the pipeline maps to a status (the factory
/// throw stays defense-in-depth). Every failure is <see cref="ErrorKind.Validation"/>. SPEC-DRIVEN —
/// RED until FrameSlotGrounding ships in Resumes.Improvement.Abstractions.
/// </summary>
public class FrameSlotGroundingTests
{
    private static Result ValidateMeasure(
        IReadOnlyDictionary<string, string>? slots = null,
        string beforeLine = FrameFixtures.MeasureLine,
        IReadOnlySet<string>? strongVerbs = null) =>
        FrameSlotGrounding.Validate(
            FrameFixtures.MeasureAntalPerPeriod(),
            slots ?? FrameFixtures.MeasureSlots(),
            beforeLine,
            strongVerbs ?? FrameFixtures.StrongVerbs("skickade"));

    private static Result ValidateSentence(
        IReadOnlyDictionary<string, string>? slots = null,
        string beforeLine = FrameFixtures.WeakLine,
        IReadOnlySet<string>? strongVerbs = null) =>
        FrameSlotGrounding.Validate(
            FrameFixtures.SentenceLedde(),
            slots ?? FrameFixtures.LeddeSlots(),
            beforeLine,
            strongVerbs ?? FrameFixtures.StrongVerbs("ledde"));

    // ===============================================================
    // Success — both frame arms
    // ===============================================================

    [Fact]
    public void Validate_ShouldSucceed_WhenMeasureSlotsAreGroundedNumericAndVerbEndorsed()
    {
        ValidateMeasure().IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Validate_ShouldSucceed_WhenSentenceSlotsAreGroundedAndFixedVerbEndorsed()
    {
        ValidateSentence().IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Validate_ShouldSucceed_WhenNumberUsesADecimalComma()
    {
        ValidateMeasure(FrameFixtures.MeasureSlots(antal: "3,5")).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Validate_ShouldSucceed_WhenTheTextSlotCarriesFreeUserEcho()
    {
        // The measure "period" is FrameSlotKind.Text — ungrounded, unconstrained beyond
        // non-whitespace. Even a personnummer-shaped value passes grounding (the guard/redactor,
        // not grounding, is the personnummer defence — this pins that grounding stays out of it).
        ValidateMeasure(FrameFixtures.MeasureSlots(period: "19811218-9876")).IsSuccess.ShouldBeTrue();
    }

    // ===============================================================
    // (a) FrameSlotNotGrounded
    // ===============================================================

    [Fact]
    public void Validate_ShouldReturnFrameSlotNotGrounded_WhenANounSlotIsNotAToken()
    {
        var slots = FrameFixtures.MeasureSlots();
        slots["enhet"] = "obefintlig"; // not a token of MeasureLine

        var result = ValidateMeasure(slots);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.FrameSlotNotGrounded");
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
    }

    [Fact]
    public void Validate_ShouldReturnFrameSlotNotGrounded_WhenANounSlotIsOnlyASubstring()
    {
        // Unicode-aware word boundary: "pär" is a substring of "päron" but not a token of the line
        // (åäö are word letters). The other noun slot ("enhet" = kollin) is grounded, so only the
        // substring slot fails.
        var slots = FrameFixtures.MeasureSlots();
        slots["vad"] = "pär";

        var result = ValidateMeasure(slots, beforeLine: "Räknade päron och kollin i lager");

        result.Error.Code.ShouldBe("Resume.FrameSlotNotGrounded");
    }

    // ===============================================================
    // (b) FrameVerbNotStrong
    // ===============================================================

    [Fact]
    public void Validate_ShouldReturnFrameVerbNotStrong_WhenMeasureVerbSlotIsNotEndorsed()
    {
        var result = ValidateMeasure(
            FrameFixtures.MeasureSlots(verb: "gjorde"),
            strongVerbs: FrameFixtures.StrongVerbs("skickade"));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.FrameVerbNotStrong");
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
    }

    [Fact]
    public void Validate_ShouldReturnFrameVerbNotStrong_WhenSentenceFixedVerbIsNotEndorsed()
    {
        // The Sentence frame's fixed verb ("ledde") is what the membership check resolves.
        var result = ValidateSentence(strongVerbs: FrameFixtures.StrongVerbs("ansvarade för"));

        result.Error.Code.ShouldBe("Resume.FrameVerbNotStrong");
    }

    // ===============================================================
    // (c) FrameNumberInvalid
    // ===============================================================

    [Theory]
    [InlineData("1 000")]
    [InlineData("3.5.1")]
    [InlineData("tolv")]
    public void Validate_ShouldReturnFrameNumberInvalid_WhenAntalBreaksTheDecimalShape(string antal)
    {
        var result = ValidateMeasure(FrameFixtures.MeasureSlots(antal: antal));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.FrameNumberInvalid");
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
    }

    // ===============================================================
    // FrameSlotArityMismatch (missing / extra / whitespace)
    // ===============================================================

    [Fact]
    public void Validate_ShouldReturnFrameSlotArityMismatch_WhenASlotKeyIsMissing()
    {
        var slots = FrameFixtures.MeasureSlots();
        slots.Remove("period");

        var result = ValidateMeasure(slots);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.FrameSlotArityMismatch");
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
    }

    [Fact]
    public void Validate_ShouldReturnFrameSlotArityMismatch_WhenAnExtraSlotKeyIsPresent()
    {
        var slots = FrameFixtures.MeasureSlots();
        slots["extra"] = "x";

        var result = ValidateMeasure(slots);

        result.Error.Code.ShouldBe("Resume.FrameSlotArityMismatch");
    }

    [Fact]
    public void Validate_ShouldReturnFrameSlotArityMismatch_WhenASlotValueIsWhitespace()
    {
        var slots = FrameFixtures.MeasureSlots();
        slots["vad"] = "   ";

        var result = ValidateMeasure(slots);

        result.Error.Code.ShouldBe("Resume.FrameSlotArityMismatch");
    }
}
