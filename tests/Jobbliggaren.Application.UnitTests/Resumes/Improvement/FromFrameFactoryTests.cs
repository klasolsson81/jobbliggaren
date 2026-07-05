using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Improvement.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Improvement;

/// <summary>
/// Fas 4b PR-7 (#656, ADR 0093 §D2 — the THIRD provenance arm). The no-synthesis guards on
/// <see cref="ProposedChange.FromFrame"/>, siblings of the <see cref="ProposedChangeFactoryTests"/>
/// FromKnowledgeBank/FromStructuralOp guards. The factory BUILDS the After itself from
/// <c>frame.Template</c> (the caller can never pass a pre-built After — synthesis is unrepresentable
/// by shape) and THROWS unless three invariants hold: (a) every noun slot word is a word-boundary
/// token of the cited Before line, (b) the resolved verb is in the strong-verb closure, (c) every
/// number slot matches the Swedish decimal shape and survives VERBATIM. A <c>FrameSlotKind.Text</c>
/// slot is a free user echo — grounded by neither (a) nor a format rule (the user-echo contract).
///
/// The guard messages cite ADR 0093 (parity with the existing FromKnowledgeBank/FromStructuralOp
/// guards citing "0074"). SPEC-DRIVEN — RED until UserParameterizedFrameProvenance +
/// ProposedChangeKind.FrameRewrite + ProposedChange.FromFrame ship in
/// Jobbliggaren.Application.Resumes.Improvement.Abstractions.
/// </summary>
public class FromFrameFactoryTests
{
    private const string TargetId = "frame:0";
    private const string Rationale = "Svagt/omätt öppningsverb — omskrivet via mening-ram";

    private static TextSpanEvidence Line(string quote) =>
        new(new TextSpan(0, quote.Length, quote), Note: null);

    private static ProposedChange BuildSentence(
        IReadOnlyDictionary<string, string>? slots = null,
        string beforeLine = FrameFixtures.WeakLine,
        IReadOnlySet<string>? strongVerbs = null,
        string targetId = TargetId) =>
        ProposedChange.FromFrame(
            targetId,
            RubricCategory.Content,
            criterionId: "A2",
            evidence: Line(beforeLine),
            frame: FrameFixtures.SentenceLedde(),
            slotInputs: slots ?? FrameFixtures.LeddeSlots(),
            strongVerbSet: strongVerbs ?? FrameFixtures.StrongVerbs("ledde"),
            rationale: Rationale);

    private static ProposedChange BuildMeasure(
        IReadOnlyDictionary<string, string>? slots = null,
        string beforeLine = FrameFixtures.MeasureLine,
        IReadOnlySet<string>? strongVerbs = null) =>
        ProposedChange.FromFrame(
            TargetId,
            RubricCategory.Content,
            criterionId: "A1",
            evidence: Line(beforeLine),
            frame: FrameFixtures.MeasureAntalPerPeriod(),
            slotInputs: slots ?? FrameFixtures.MeasureSlots(),
            strongVerbSet: strongVerbs ?? FrameFixtures.StrongVerbs("skickade"),
            rationale: Rationale);

    // ===============================================================
    // Happy path — the factory BUILDS the After and stamps the third-arm provenance
    // ===============================================================

    [Fact]
    public void FromFrame_ShouldBuildAfterFromTemplate_WhenSentenceFrameSlotsAreValid()
    {
        var change = BuildSentence();

        change.Kind.ShouldBe(ProposedChangeKind.FrameRewrite);
        change.Operation.ShouldBeNull("a frame rewrite is a text replacement, never a structural op.");
        change.Replacement.ShouldNotBeNull();
        // Before is the cited line verbatim; After is the mechanically substituted template.
        change.Replacement!.Before.ShouldBe(FrameFixtures.WeakLine);
        change.Replacement.After.ShouldBe(FrameFixtures.LeddeAfter);
    }

    [Fact]
    public void FromFrame_ShouldCarryFrameProvenance_WithFrameIdVerbAndUserInputs()
    {
        var slots = FrameFixtures.LeddeSlots();

        var change = BuildSentence(slots);

        var provenance = change.Provenance.ShouldBeOfType<UserParameterizedFrameProvenance>();
        provenance.FrameId.ShouldBe("sentence-ledde");
        // A sentence frame carries the FIXED lead verb (frame.Verb), not a user-filled slot.
        provenance.Verb.ShouldBe("ledde");
        provenance.UserInputs.ShouldBe(slots);
    }

    [Fact]
    public void FromFrame_ShouldUppercaseOnlyTheFirstCharacter_WhenMeasureVerbSlotIsLowercase()
    {
        // The measure template starts with {verb}; the user's verb echo "skickade" is lowercase,
        // so the pure mechanical transform capitalises the first character only.
        var change = BuildMeasure(FrameFixtures.MeasureSlots(verb: "skickade", antal: "30", period: "dag"));

        change.Replacement!.After.ShouldBe("Skickade paket av 30 kollin per dag.");
    }

    [Fact]
    public void FromFrame_ShouldResolveVerbFromTheVerbSlot_WhenMeasureFrame()
    {
        // A measure frame has frame.Verb == null; the resolved verb is the single Verb-slot input.
        var change = BuildMeasure(FrameFixtures.MeasureSlots(verb: "skickade"));

        change.Provenance.ShouldBeOfType<UserParameterizedFrameProvenance>().Verb.ShouldBe("skickade");
    }

    [Fact]
    public void FromFrame_ShouldKeepTheNumberVerbatim_WhenAntalUsesADecimalComma()
    {
        // Invariant c: a Swedish decimal comma is allowed AND must survive byte-identical — never
        // reformatted to "3.5" or "35" ("aldrig påhittade siffror", ADR 0093 §D2).
        var change = BuildMeasure(FrameFixtures.MeasureSlots(antal: "3,5"));

        change.Replacement!.After.ShouldContain("3,5");
        change.Replacement.After.ShouldNotContain("3.5");
    }

    [Fact]
    public void FromFrame_ShouldAllowAPersonnummerShapedTextSlot_WhenTheSlotIsAFreeEcho()
    {
        // A FrameSlotKind.Text slot (the measure "period") rides the user-echo contract: no
        // grounding, no format rule. A personnummer typed here is NOT rejected at construction —
        // it is the Application-boundary personnummer guard (apply) / redactor (preview) that
        // catches it downstream, never this factory (which would otherwise mask a value it must
        // preserve verbatim for the guard to see).
        var act = () => BuildMeasure(FrameFixtures.MeasureSlots(period: "19811218-9876"));

        act.ShouldNotThrow();
        act().Replacement!.After.ShouldContain("19811218-9876");
    }

    [Fact]
    public void FromFrame_ShouldGroundAMultiWordNounSlot_WhenEveryWordIsATokenOfTheLine()
    {
        // A noun slot may carry several words; invariant a requires EACH to be a token of the line.
        var slots = FrameFixtures.LeddeSlots();
        slots["del1"] = "kundtjänst support"; // both words are tokens of WeakLine

        var act = () => BuildSentence(slots);

        act.ShouldNotThrow();
    }

    // ===============================================================
    // (a) noun grounding — never a synthesised noun
    // ===============================================================

    [Fact]
    public void FromFrame_ShouldThrow_WhenANounSlotWordIsNotAWordBoundaryTokenOfTheLine()
    {
        var slots = FrameFixtures.LeddeSlots();
        slots["del4"] = "obefintlig"; // not present in WeakLine

        var act = () => BuildSentence(slots);

        act.ShouldThrow<ArgumentException>()
            .Message.ShouldContain("0093");
    }

    [Fact]
    public void FromFrame_ShouldThrow_WhenAMultiWordNounSlotHasOneUngroundedWord()
    {
        var slots = FrameFixtures.LeddeSlots();
        slots["del1"] = "kundtjänst saknas"; // "saknas" is not a token of WeakLine

        var act = () => BuildSentence(slots);

        act.ShouldThrow<ArgumentException>().Message.ShouldContain("0093");
    }

    [Fact]
    public void FromFrame_ShouldThrow_WhenANounSlotIsOnlyASubstringNotAWholeToken()
    {
        // Word-boundary, Unicode-aware: "kund" is a substring of "kundtjänst" but not a token —
        // it must NOT ground (parity ReviewText.ContainsWord semantics, åäö are word letters).
        var slots = FrameFixtures.LeddeSlots();
        slots["del1"] = "kund";

        var act = () => BuildSentence(slots);

        act.ShouldThrow<ArgumentException>().Message.ShouldContain("0093");
    }

    // ===============================================================
    // (b) verb membership — never an unendorsed verb
    // ===============================================================

    [Fact]
    public void FromFrame_ShouldThrow_WhenResolvedVerbIsNotInTheStrongVerbSet()
    {
        // Sentence frame: resolved verb = "ledde" (frame.Verb); an empty closure fails invariant b.
        var act = () => BuildSentence(strongVerbs: FrameFixtures.StrongVerbs());

        act.ShouldThrow<ArgumentException>().Message.ShouldContain("0093");
    }

    [Fact]
    public void FromFrame_ShouldThrow_WhenMeasureVerbSlotValueIsNotInTheStrongVerbSet()
    {
        // Measure frame: resolved verb = the Verb-slot input; a non-endorsed verb fails invariant b.
        var act = () => BuildMeasure(
            FrameFixtures.MeasureSlots(verb: "gjorde"),
            strongVerbs: FrameFixtures.StrongVerbs("skickade"));

        act.ShouldThrow<ArgumentException>().Message.ShouldContain("0093");
    }

    // ===============================================================
    // (c) number verbatim shape
    // ===============================================================

    [Theory]
    [InlineData("1 000")]     // thousands separator (space) not allowed
    [InlineData("3.5.1")]     // not a number
    [InlineData("abc")]       // not a number
    [InlineData("1234567890")] // 10 digits — over the 1–9 integer-part bound
    public void FromFrame_ShouldThrow_WhenANumberSlotBreaksTheDecimalShape(string antal)
    {
        var act = () => BuildMeasure(FrameFixtures.MeasureSlots(antal: antal));

        act.ShouldThrow<ArgumentException>().Message.ShouldContain("0093");
    }

    // ===============================================================
    // Arity + presence guards
    // ===============================================================

    [Fact]
    public void FromFrame_ShouldThrow_WhenASlotKeyIsMissing()
    {
        var slots = FrameFixtures.LeddeSlots();
        slots.Remove("del4");

        var act = () => BuildSentence(slots);

        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void FromFrame_ShouldThrow_WhenAnExtraSlotKeyIsPresent()
    {
        var slots = FrameFixtures.LeddeSlots();
        slots["bonus"] = "extra";

        var act = () => BuildSentence(slots);

        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void FromFrame_ShouldThrow_WhenASlotValueIsWhitespace()
    {
        var slots = FrameFixtures.LeddeSlots();
        slots["del1"] = "   ";

        var act = () => BuildSentence(slots);

        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void FromFrame_ShouldThrow_WhenTheCitedQuoteIsWhitespace()
    {
        var act = () => BuildSentence(beforeLine: "   ");

        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void FromFrame_ShouldThrow_WhenTargetIdIsWhitespace()
    {
        var act = () => BuildSentence(targetId: "   ");

        act.ShouldThrow<ArgumentException>();
    }
}
