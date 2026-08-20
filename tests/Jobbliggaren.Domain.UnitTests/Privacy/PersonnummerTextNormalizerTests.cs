using Jobbliggaren.Domain.Privacy;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Privacy;

// Fas 4 STEG 8 (F4-8, ADR 0074 Invariant 1) — the spaced-form personnummer
// call-site. F4-1 deferred the "spaced/OCR-gapped false-negative" here:
// PersonnummerTextNormalizer.Normalize bridges a (8-or-6 digits)(0–2 space
// separators or tabs)(4 digits) gap on a TRANSIENT scan-copy so the UNCHANGED
// context-free PersonnummerScanner.Scan can then FLAG the form. The safety stays
// in the untouched validation layer (Personnummer.TryParse date+Luhn), so bridging
// can never manufacture a VALID false positive out of two unrelated numbers.
//
// SPEC-DRIVEN: these assert the documented behaviour (scanner blind to spaced
// form directly, sees it after Normalize; idempotence; determinism; newline NOT
// bridged; no false positive). All vectors are SYNTHETIC Luhn-valid test numbers
// reused from PersonnummerScannerTests / PersonnummerTests (no real identities).
public class PersonnummerTextNormalizerTests
{
    // The canonical valid vectors from the existing scanner/validation suites.
    // Contiguous they pass date+Luhn; here a single space is inserted before the
    // final 4 digits to model the spaced/OCR-gapped form.
    private const string ValidPnr12Contiguous = "198112189876"; // 19811218-9876
    private const string ValidPnr10Contiguous = "8112189876"; // 811218-9876

    // ===============================================================
    // The load-bearing F4-8 behaviour: scanner blind to the spaced form
    // directly, but sees it AFTER Normalize bridges the gap.
    // ===============================================================

    [Theory]
    [InlineData("198112189876")] // 12-digit YYYYMMDD NNNN form
    [InlineData("8112189876")] // 10-digit YYMMDD NNNN form
    public void Scan_SpacedValidPersonnummer_FalseNegativeDirectly_FlaggedAfterNormalize(
        string contiguous)
    {
        // Insert a single space before the final 4 digits → the spaced form.
        var spaced = $"{contiguous[..^4]} {contiguous[^4..]}";
        var text = $"Personnummer: {spaced} (uppgift i CV).";

        // Directly: the context-free scanner does NOT bridge the space, so the
        // spaced personnummer is a false negative (the F4-1 gap this step closes).
        PersonnummerScanner.Scan(text).ShouldBeEmpty();

        // After Normalize bridges the gap, the SAME unchanged scanner flags it.
        var normalized = PersonnummerTextNormalizer.Normalize(text, PersonnummerGapProfile.ExtractedDocumentText);
        var matches = PersonnummerScanner.Scan(normalized);

        var match = matches.ShouldHaveSingleItem();
        match.Kind.ShouldBe(PersonnummerKind.Personnummer);
    }

    [Fact]
    public void Scan_SpacedValidSamordningsnummer_FlaggedOnlyAfterNormalize()
    {
        // 811278-9873 is a Luhn-valid samordningsnummer (day 18+60=78); spaced form.
        const string spaced = "811278 9873";
        var text = $"Samordningsnummer {spaced} i dokumentet.";

        PersonnummerScanner.Scan(text).ShouldBeEmpty();

        var matches = PersonnummerScanner.Scan(PersonnummerTextNormalizer.Normalize(text, PersonnummerGapProfile.ExtractedDocumentText));

        var match = matches.ShouldHaveSingleItem();
        match.Kind.ShouldBe(PersonnummerKind.Samordningsnummer);
    }

    [Fact]
    public void Scan_TabSeparatedValidPersonnummer_FlaggedAfterNormalize()
    {
        // A tab is the other OCR-gap shape the normalizer bridges ([\p{Zs}\t]{0,2}).
        const string spaced = "811218\t9876";
        var text = $"Pnr {spaced}.";

        PersonnummerScanner.Scan(text).ShouldBeEmpty();

        PersonnummerScanner.Scan(PersonnummerTextNormalizer.Normalize(text, PersonnummerGapProfile.ExtractedDocumentText))
            .ShouldHaveSingleItem()
            .Kind.ShouldBe(PersonnummerKind.Personnummer);
    }

    // ===============================================================
    // #268 C1 (ADR 0074 Invariant 1): a Unicode SPACE SEPARATOR (\p{Zs}) gap —
    // the NON-BREAKING SPACE (U+00A0) this product emits as its own digit-group
    // separator, plus narrow-NBSP / thin / figure / en space, all of which
    // PDF/DOCX extraction passes through verbatim — is now bridged, so the spaced
    // personnummer is FLAGGED instead of silently stored as "no personnummer found".
    // Before the fix the bridge class was ASCII [ \t] only, so these slipped through.
    // The separators are written as \u escapes so each distinct code point is explicit.
    // ===============================================================

    [Theory]
    [InlineData("811218\u00A09876")] // U+00A0 NO-BREAK SPACE — the Swedish digit-group separator this app emits
    [InlineData("811218\u202F9876")] // U+202F NARROW NO-BREAK SPACE
    [InlineData("811218\u20099876")] // U+2009 THIN SPACE
    [InlineData("811218\u20079876")] // U+2007 FIGURE SPACE
    [InlineData("811218\u20029876")] // U+2002 EN SPACE
    public void Scan_UnicodeSpaceSeparatedPersonnummer_FalseNegativeDirectly_FlaggedAfterNormalize(
        string spaced)
    {
        var text = $"Personnummer {spaced} i CV.";

        // Directly: the context-free scanner does not bridge the gap → false negative.
        PersonnummerScanner.Scan(text).ShouldBeEmpty();

        // After Normalize joins the digits, the SAME unchanged scanner flags it.
        PersonnummerScanner.Scan(PersonnummerTextNormalizer.Normalize(text, PersonnummerGapProfile.ExtractedDocumentText))
            .ShouldHaveSingleItem()
            .Kind.ShouldBe(PersonnummerKind.Personnummer);
    }

    [Fact]
    public void Scan_NonBreakingSpaceSeparated12DigitPersonnummer_FlaggedAfterNormalize()
    {
        // The 12-digit full-century form gapped by U+00A0 (the exact #268 C1 vector).
        const string text = "19811218\u00A09876";

        PersonnummerScanner.Scan(text).ShouldBeEmpty();

        PersonnummerScanner.Scan(PersonnummerTextNormalizer.Normalize(text, PersonnummerGapProfile.ExtractedDocumentText))
            .ShouldHaveSingleItem()
            .Kind.ShouldBe(PersonnummerKind.Personnummer);
    }

    [Fact]
    public void Scan_TwoCharGapNbspThenSpace_FlaggedAfterNormalize()
    {
        // A two-character gap mixing an NBSP and an ASCII space is within the {0,2}
        // bound and is bridged (digit-group separator immediately before a stray space).
        const string text = "Pnr 811218\u00A0 9876.";

        PersonnummerScanner.Scan(PersonnummerTextNormalizer.Normalize(text, PersonnummerGapProfile.ExtractedDocumentText))
            .ShouldHaveSingleItem()
            .Kind.ShouldBe(PersonnummerKind.Personnummer);
    }

    // ===============================================================
    // No false positive: two UNRELATED adjacent numbers that do NOT together
    // form a date+Luhn-valid personnummer are NOT manufactured into a match —
    // because TryParse's date+Luhn gate is untouched (true for the widened class too).
    // ===============================================================

    [Fact]
    public void Scan_TwoUnrelatedAdjacentNumbers_NotManufacturedIntoMatch_AfterNormalize()
    {
        // 8-digit run + ASCII space + 4-digit run that, joined, fails date-sanity/Luhn.
        // "12345678 0000" → joined 123456780000: month field "34" is impossible,
        // so TryParse rejects it regardless of the bridge. The bridge changes only
        // which candidate the scanner sees; the validation gate stays the law.
        const string text = "Referens 12345678 0000 i systemet.";

        var normalized = PersonnummerTextNormalizer.Normalize(text, PersonnummerGapProfile.ExtractedDocumentText);

        // The gap is bridged (candidate shaping) ...
        normalized.ShouldContain("123456780000");
        // ... but the scanner still reports nothing (date+Luhn gate rejects it).
        PersonnummerScanner.Scan(normalized).ShouldBeEmpty();
    }

    [Fact]
    public void Scan_TwoUnrelatedNumbers_NonBreakingSpaceGap_NotManufacturedIntoMatch()
    {
        // The widened \p{Zs} bridge still cannot manufacture a valid personnummer: the
        // joined "123456780000" fails date-sanity ("34" is no month), so the Luhn/date
        // gate rejects it exactly as for the ASCII-space case.
        const string text = "Referens 12345678\u00A00000 i systemet.";

        var normalized = PersonnummerTextNormalizer.Normalize(text, PersonnummerGapProfile.ExtractedDocumentText);

        normalized.ShouldContain("123456780000");
        PersonnummerScanner.Scan(normalized).ShouldBeEmpty();
    }

    [Fact]
    public void Scan_SpacedPhoneLikeFourPlusFour_NotBridged_NoMatch()
    {
        // A 4+4 split (e.g. part of a phone number) is NOT the 6/8 + 4 shape, so
        // the normalizer does not bridge it — and nothing is flagged.
        const string text = "Mobil 0701 234567 dagtid.";

        var normalized = PersonnummerTextNormalizer.Normalize(text, PersonnummerGapProfile.ExtractedDocumentText);

        PersonnummerScanner.Scan(normalized).ShouldBeEmpty();
    }

    // ===============================================================
    // A newline is a field/line boundary, not an OCR gap — NOT bridged.
    // ===============================================================

    [Theory]
    [InlineData("811218\n9876")] // LF
    [InlineData("811218\r\n9876")] // CRLF
    public void Normalize_NewlineBetweenDigitRuns_NotBridged(string spaced)
    {
        var text = $"Kontakt\n{spaced}\nslut";

        var normalized = PersonnummerTextNormalizer.Normalize(text, PersonnummerGapProfile.ExtractedDocumentText);

        // The digit runs stay separated by the newline — never joined.
        normalized.ShouldNotContain("8112189876");
        PersonnummerScanner.Scan(normalized).ShouldBeEmpty();
    }

    // ===============================================================
    // Idempotence + determinism (a joined token has no gap left to bridge).
    // ===============================================================

    [Theory]
    [InlineData("Personnummer 198112189876 redan ihopskrivet.")] // already contiguous
    [InlineData("Personnummer 19811218 9876 spaced.")] // spaced form
    [InlineData("Två nummer 811218 9876 och 811278 9873.")] // two spaced forms
    [InlineData("Ingen siffra alls i denna text.")] // nothing to bridge
    public void Normalize_IsIdempotent(string text)
    {
        var once = PersonnummerTextNormalizer.Normalize(text, PersonnummerGapProfile.ExtractedDocumentText);
        var twice = PersonnummerTextNormalizer.Normalize(once, PersonnummerGapProfile.ExtractedDocumentText);

        twice.ShouldBe(once);
    }

    [Fact]
    public void Normalize_SameInput_ProducesSameOutput_Deterministic()
    {
        const string text = "Pnr 19811218 9876 och samordning 811278 9873 i CV.";

        var first = PersonnummerTextNormalizer.Normalize(text, PersonnummerGapProfile.ExtractedDocumentText);
        var second = PersonnummerTextNormalizer.Normalize(text, PersonnummerGapProfile.ExtractedDocumentText);

        second.ShouldBe(first);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \t  ")]
    [InlineData("Bara prosa utan personnummer.")]
    public void Normalize_TextWithoutBridgeableGap_ReturnsTextUnchanged(string text)
    {
        PersonnummerTextNormalizer.Normalize(text, PersonnummerGapProfile.ExtractedDocumentText).ShouldBe(text);
    }

    // Guards against future regressions of the reused constants (keeps the
    // spaced-vs-contiguous derivation honest: the contiguous forms must validate).
    [Fact]
    public void Sanity_ContiguousVectorsAreValid_SoTheSpacedDerivationIsMeaningful()
    {
        PersonnummerScanner.Scan(ValidPnr12Contiguous).ShouldHaveSingleItem();
        PersonnummerScanner.Scan(ValidPnr10Contiguous).ShouldHaveSingleItem();
    }

    // ===============================================================
    // #427 V2 (ADR 0074 Invariant 1): zero-width FORMAT characters (\p{Cf}) — e.g.
    // U+200B ZERO WIDTH SPACE, U+FEFF ZERO WIDTH NO-BREAK SPACE, U+200C/D, U+2060 —
    // that PDF/DOCX extraction emits are NOT in the \p{Zs} space-separator class, so a
    // zero-width-gapped personnummer was a false negative (the import guard would store
    // the CV flagged "no personnummer found"). Normalize now STRIPS \p{Cf} from the
    // transient scan-copy first, so the SAME unchanged scanner flags the joined digits.
    // Separators written as \u escapes so each code point is explicit.
    // ===============================================================

    [Theory]
    [InlineData("811218\u200B9876")] // U+200B ZERO WIDTH SPACE
    [InlineData("811218\uFEFF9876")] // U+FEFF ZERO WIDTH NO-BREAK SPACE
    [InlineData("811218\u200C9876")] // U+200C ZERO WIDTH NON-JOINER
    [InlineData("811218\u200D9876")] // U+200D ZERO WIDTH JOINER
    [InlineData("811218\u20609876")] // U+2060 WORD JOINER
    public void Scan_ZeroWidthSeparatedPersonnummer_FalseNegativeDirectly_FlaggedAfterNormalize(
        string spaced)
    {
        var text = $"Personnummer {spaced} i CV.";

        // Directly: the context-free scanner does not bridge the zero-width gap → false negative.
        PersonnummerScanner.Scan(text).ShouldBeEmpty();

        // After Normalize strips the zero-width char, the SAME unchanged scanner flags it.
        PersonnummerScanner.Scan(PersonnummerTextNormalizer.Normalize(text, PersonnummerGapProfile.ExtractedDocumentText))
            .ShouldHaveSingleItem()
            .Kind.ShouldBe(PersonnummerKind.Personnummer);
    }

    [Fact]
    public void Scan_ZeroWidthSeparated12DigitPersonnummer_FlaggedAfterNormalize()
    {
        // The 12-digit full-century form gapped by U+200B (a PDF/DOCX extraction artefact).
        const string text = "19811218\u200B9876";

        PersonnummerScanner.Scan(text).ShouldBeEmpty();

        PersonnummerScanner.Scan(PersonnummerTextNormalizer.Normalize(text, PersonnummerGapProfile.ExtractedDocumentText))
            .ShouldHaveSingleItem()
            .Kind.ShouldBe(PersonnummerKind.Personnummer);
    }

    [Fact]
    public void Scan_ZeroWidthThenNonBreakingSpaceGap_FlaggedAfterNormalize()
    {
        // A zero-width char adjacent to the NBSP digit-group separator: strip the ZW first,
        // then the {0,2} \p{Zs} bridge joins the digits.
        const string text = "Pnr 811218\u200B\u00A09876.";

        PersonnummerScanner.Scan(PersonnummerTextNormalizer.Normalize(text, PersonnummerGapProfile.ExtractedDocumentText))
            .ShouldHaveSingleItem()
            .Kind.ShouldBe(PersonnummerKind.Personnummer);
    }

    [Fact]
    public void Scan_TwoUnrelatedNumbers_ZeroWidthGap_NotManufacturedIntoMatch()
    {
        // Stripping \p{Cf} cannot manufacture a valid personnummer: the joined
        // "123456780000" fails date-sanity ("34" is no month), so the untouched date+Luhn
        // gate rejects it — the widening is candidate SHAPING only.
        const string text = "Referens 12345678\u200B0000 i systemet.";

        var normalized = PersonnummerTextNormalizer.Normalize(text, PersonnummerGapProfile.ExtractedDocumentText);

        normalized.ShouldContain("123456780000");
        PersonnummerScanner.Scan(normalized).ShouldBeEmpty();
    }

    [Fact]
    public void Normalize_ZeroWidthGapped_IsIdempotent()
    {
        const string text = "Personnummer 811218\u200B9876 i CV.";

        var once = PersonnummerTextNormalizer.Normalize(text, PersonnummerGapProfile.ExtractedDocumentText);
        var twice = PersonnummerTextNormalizer.Normalize(once, PersonnummerGapProfile.ExtractedDocumentText);

        twice.ShouldBe(once);
    }

    // #427 V3 (accepted residual): a 3+ VISIBLE-column gap is deliberately NOT bridged in
    // EXTRACTED DOCUMENT TEXT. Re-adjudicated rather than inherited in #1415 and re-affirmed
    // on measured ground (ADR 0134): PersonnummerBridgeCollisionRateTests measures how much
    // likelier a bridged date column is to collide than arbitrary digits are. The
    // zero-width strip does not change this — the {0,2} bound governs the visible \p{Zs}\t
    // separators only.
    [Fact]
    public void Normalize_ThreeVisibleColumnGap_NotBridged()
    {
        const string text = "Pnr 811218   9876 slut.";

        var normalized = PersonnummerTextNormalizer.Normalize(text, PersonnummerGapProfile.ExtractedDocumentText);

        normalized.ShouldNotContain("8112189876");
        PersonnummerScanner.Scan(normalized).ShouldBeEmpty();
    }

    // ===============================================================
    // #1415 / ADR 0134 — the SingleLineUserInput profile.
    //
    // The residual above is a property of EXTRACTED DOCUMENT TEXT, where a line break is a
    // field boundary and a date column above four digits is a real layout. A hand-typed
    // search box has neither, and its value persists in plaintext outside ADR 0049's envelope
    // and re-renders verbatim as the /sokningar row label — so the same gap forms that are an
    // acceptable residual in a CV are a PII leak there.
    //
    // These are the TWELVE gap classes measured unflagged across the whole product on
    // 2026-08-20 (#1415). The last three were not in the issue's own list and were found by
    // re-measuring rather than by inheriting it. A three-space gap in free text is NOT a
    // separate row: it is the same gap and measures identically.
    // ===============================================================

    [Theory]
    [InlineData("Pnr 811218   9876 slut.", "three spaces")]
    [InlineData("Pnr 811218    9876 slut.", "four spaces")]
    [InlineData("Pnr 811218     9876 slut.", "five spaces")]
    [InlineData("Pnr 811218\t\t\t9876 slut.", "three tabs")]
    [InlineData("Pnr 811218\n9876 slut.", "U+000A LINE FEED")]
    [InlineData("Pnr 811218\r\n9876 slut.", "CRLF")]
    [InlineData("Pnr 811218\r9876 slut.", "U+000D CARRIAGE RETURN")]
    [InlineData("Pnr 811218\u00019876 slut.", "U+0001 Cc control")]
    [InlineData("Pnr 811218 \u0001 9876 slut.", "space Cc space")]
    [InlineData("Pnr 811218\u20289876 slut.", "U+2028 LINE SEPARATOR")]
    [InlineData("Pnr 811218\u000B9876 slut.", "U+000B LINE TABULATION")]
    [InlineData("Pnr 811218\u000C9876 slut.", "U+000C FORM FEED")]
    public void Normalize_SingleLineUserInput_BridgesWiderGaps_AndFlags(string text, string label)
    {
        var normalized = PersonnummerTextNormalizer.Normalize(text, PersonnummerGapProfile.SingleLineUserInput);

        normalized.ShouldContain(
            "8112189876",
            customMessage: $"the {label} gap must be bridged on this profile");
        PersonnummerScanner.Scan(normalized).ShouldHaveSingleItem();
    }

    // U+2028 is why the gap class is [\s\p{Cc}] and not the otherwise-natural
    // [\p{Zs}\p{Cc}]: LINE SEPARATOR lives in \p{Zl}, which is in NEITHER of those two
    // categories but IS in .NET's \s. Written as its own test because a class that silently
    // dropped \s for \p{Zs} would leave the theory above passing on eleven of twelve rows,
    // and eleven-of-twelve is exactly the shape a reviewer reads as green.
    [Fact]
    public void Normalize_SingleLineUserInput_LineSeparatorIsInTheGapClass_NotOnlyZsAndCc()
    {
        const char lineSeparator = '\u2028';
        char.GetUnicodeCategory(lineSeparator).ShouldBe(System.Globalization.UnicodeCategory.LineSeparator);

        var normalized = PersonnummerTextNormalizer.Normalize(
            $"Pnr 811218{lineSeparator}9876 slut.", PersonnummerGapProfile.SingleLineUserInput);

        PersonnummerScanner.Scan(normalized).ShouldHaveSingleItem();
    }

    // ADR 0134 R3: the {0,8} bound is the ONE number this profile introduces, and a bound that
    // nothing crosses is indistinguishable from an unbounded quantifier. Both sides, so the
    // residual above it is pinned as a residual and not merely unmentioned.
    [Fact]
    public void Normalize_SingleLineUserInput_BoundIsEightNotUnbounded()
    {
        var eight = PersonnummerTextNormalizer.Normalize(
            "Pnr 811218" + new string(' ', 8) + "9876 slut.", PersonnummerGapProfile.SingleLineUserInput);
        PersonnummerScanner.Scan(eight).ShouldHaveSingleItem();

        var nine = PersonnummerTextNormalizer.Normalize(
            "Pnr 811218" + new string(' ', 9) + "9876 slut.", PersonnummerGapProfile.SingleLineUserInput);
        PersonnummerScanner.Scan(nine).ShouldBeEmpty(
            "a gap wider than eight is ADR 0134 R3 — a declared residual, not an oversight");
    }

    // The counterexample that decided #1414's raw-vs-residual question, kept as a POSITIVE.
    // It is why \p{Cc} sits in the gap class and must never move to the \p{Cf} strip: stripping
    // the control character would glue the trailing digit onto the candidate and the (?!\d)
    // boundary would then reject the whole form.
    [Fact]
    public void Normalize_ControlCharThenTrailingDigit_StaysFlagged_OnBothProfiles()
    {
        const string text = "Pnr 811218 9876\u00015 slut.";

        PersonnummerScanner.Scan(
            PersonnummerTextNormalizer.Normalize(text, PersonnummerGapProfile.ExtractedDocumentText))
            .ShouldHaveSingleItem();
        PersonnummerScanner.Scan(
            PersonnummerTextNormalizer.Normalize(text, PersonnummerGapProfile.SingleLineUserInput))
            .ShouldHaveSingleItem();
    }

    // The two profiles are NOT orderable by strength, and this is the pair that proves it —
    // the same string, two answers, each correct for the kind of text it names. Without this
    // a reader could take SingleLineUserInput for "the strict one" and "helpfully" apply it
    // to CV text, which is precisely the widening ADR 0134 measured as dangerous.
    [Fact]
    public void Normalize_TheSameGapAnswersDifferentlyPerProfile_ByDesign()
    {
        const string text = "Pnr 811218   9876 slut.";

        PersonnummerScanner.Scan(
            PersonnummerTextNormalizer.Normalize(text, PersonnummerGapProfile.ExtractedDocumentText))
            .ShouldBeEmpty();
        PersonnummerScanner.Scan(
            PersonnummerTextNormalizer.Normalize(text, PersonnummerGapProfile.SingleLineUserInput))
            .ShouldHaveSingleItem();
    }

    // The wider profile must not become a way to manufacture a personnummer out of two
    // unrelated numbers: the UNCHANGED date+Luhn gate is still the only authority, on both
    // profiles. "12345678" gapped onto "0000" bridges and is then rejected on month "34".
    [Theory]
    [InlineData("Referens 12345678   0000 i systemet.")]
    [InlineData("Referens 12345678\n0000 i systemet.")]
    [InlineData("Tel 0701 2345 slut.")]
    public void Normalize_SingleLineUserInput_StillCannotManufactureAValidPersonnummer(string text)
    {
        var normalized = PersonnummerTextNormalizer.Normalize(text, PersonnummerGapProfile.SingleLineUserInput);

        PersonnummerScanner.Scan(normalized).ShouldBeEmpty();
    }

    // ===============================================================
    // #427 (2nd CTO ruling) — import path: R2 (a '-'/'+' separator ADJACENT to a space,
    // "811218- 9876" / "811218 -9876") is now bridged too. R1 (zero-width between two
    // spaces) is already handled by the \p{Cf} strip + the {0,2} bridge, asserted here
    // for path symmetry. V3 (3+ visible columns) is unchanged. Gap points as \u escapes.
    // ===============================================================

    [Theory]
    [InlineData("811218- 9876")] // R2a: dash then space
    [InlineData("811218 -9876")] // R2b: space then dash
    [InlineData("811218 \u200B 9876")] // R1 symmetry: space, U+200B ZERO WIDTH SPACE, space
    public void Scan_SeparatorAdjacentOrInterleavedGap_FlaggedAfterNormalize(string gapped)
    {
        var text = $"Personnummer {gapped} i CV.";

        // Directly the context-free scanner does not bridge the gap → false negative.
        PersonnummerScanner.Scan(text).ShouldBeEmpty();

        // After Normalize joins the digits, the SAME unchanged scanner flags it.
        PersonnummerScanner.Scan(PersonnummerTextNormalizer.Normalize(text, PersonnummerGapProfile.ExtractedDocumentText))
            .ShouldHaveSingleItem()
            .Kind.ShouldBe(PersonnummerKind.Personnummer);
    }

    [Theory]
    [InlineData("Referens 12345678- 0000 i systemet.")] // R2a: dash then space
    [InlineData("Referens 12345678 -0000 i systemet.")] // R2b: space then dash
    public void Scan_TwoUnrelated_SeparatorAdjacentSpace_NotManufacturedAfterNormalize(string text)
    {
        // The widened separator-adjacent-space bridge must NOT manufacture a valid pnr:
        // "12345678- 0000" joins to 123456780000 whose month "34" fails date sanity.
        PersonnummerScanner.Scan(PersonnummerTextNormalizer.Normalize(text, PersonnummerGapProfile.ExtractedDocumentText)).ShouldBeEmpty();
    }

    // ===============================================================
    // #497 (ADR 0074 Invariant 1): a Unicode-dash separator ADJACENT to the space run
    // ("811218<endash> 9876" / "811218 <endash>9876") is now bridged, the separator SHAPE
    // class in SpacedCandidateRegex widened from ASCII [-+] to [-+\p{Pd}\u2212] (EN DASH
    // U+2013, NON-BREAKING HYPHEN U+2011, MINUS SIGN U+2212). Word and PDF/DOCX emit the
    // en-dash; before the fix these spaced Unicode-dash forms slipped both paths. \u escapes.
    // ===============================================================

    [Theory]
    [InlineData("811218\u2013 9876")] // EN DASH then space
    [InlineData("811218 \u20139876")] // space then EN DASH
    [InlineData("811218\u2212 9876")] // MINUS SIGN then space
    public void Scan_UnicodeDashAdjacentSpace_FlaggedAfterNormalize(string gapped)
    {
        var text = $"Personnummer {gapped} i CV.";

        // Directly the context-free scanner does not bridge the gap (false negative).
        PersonnummerScanner.Scan(text).ShouldBeEmpty();

        // After Normalize bridges the digits, the SAME unchanged scanner flags it.
        PersonnummerScanner.Scan(PersonnummerTextNormalizer.Normalize(text, PersonnummerGapProfile.ExtractedDocumentText))
            .ShouldHaveSingleItem()
            .Kind.ShouldBe(PersonnummerKind.Personnummer);
    }

    [Fact]
    public void Scan_ContiguousUnicodeDashPersonnummer_FlaggedWithoutNormalize()
    {
        // A CONTIGUOUS Unicode-dash form ("811218<endash>9876", no space) needs no bridge:
        // the widened CandidateRegex separator class flags it directly (parity with the
        // ASCII-hyphen contiguous form). Proves the widening reaches the contiguous flag path.
        const string text = "Personnummer 811218\u20139876 i CV.";

        PersonnummerScanner.Scan(text)
            .ShouldHaveSingleItem()
            .Kind.ShouldBe(PersonnummerKind.Personnummer);
    }

    // ===============================================================
    // #665 (STEG 1 pnr-scanner hardening; ADR 0074 Invariant 1): the TWO-separator ZERO-space
    // form "811218--9876". The redaction path (GapAwareCandidateRegex admits sep? space{0,2} sep?)
    // already masks it, but the flag path could never reach it: CandidateRegex allows one
    // separator, TryParse rejects a second, and the pre-#665 bridge required a MANDATORY space
    // ([\p{Zs}\t]{1,2}). A permitted-direction false negative (redaction superset of flag), but
    // an import-flag miss with a real PII consequence (Personnummer.Found never fires, so the 5c
    // suppression / consent path does not trigger). The bridge is now {0,2} spaces, giving the
    // normalizer structural parity with GapAwareCandidateRegex, so the joined digits ($1$2, both
    // separators dropped) are flagged by the SAME unchanged scanner. Safety unchanged: the
    // date+Luhn gate governs the joined token.
    // ===============================================================

    [Theory]
    [InlineData("811218--9876", PersonnummerKind.Personnummer)]
    [InlineData("811278--9873", PersonnummerKind.Samordningsnummer)] // day 18+60=78
    [InlineData("19811218--9876", PersonnummerKind.Personnummer)] // 12-digit full-century
    public void Scan_DoubleSeparatorNoSpace_FalseNegativeDirectly_FlaggedAfterNormalize(
        string doubleSep, PersonnummerKind kind)
    {
        var text = $"Personnummer {doubleSep} i CV.";

        // Directly: the contiguous scanner sees two separators and cannot match (false negative).
        PersonnummerScanner.Scan(text).ShouldBeEmpty();

        // After Normalize joins the digits (dropping BOTH separators), the SAME scanner flags it.
        PersonnummerScanner.Scan(PersonnummerTextNormalizer.Normalize(text, PersonnummerGapProfile.ExtractedDocumentText))
            .ShouldHaveSingleItem()
            .Kind.ShouldBe(kind);
    }

    [Fact]
    public void Scan_TwoUnrelated_DoubleSeparatorNoSpace_NotManufacturedAfterNormalize()
    {
        // Over-flag guard: "12345678--0000" joins to 123456780000, whose month field ("56" after
        // the century drop) fails date sanity, so the untouched date+Luhn gate rejects it. The
        // {0,2} widening is candidate SHAPING only.
        const string text = "Referens 12345678--0000 i systemet.";

        PersonnummerScanner.Scan(PersonnummerTextNormalizer.Normalize(text, PersonnummerGapProfile.ExtractedDocumentText)).ShouldBeEmpty();
    }

    [Fact]
    public void Normalize_DoubleSeparatorNoSpace_IsIdempotent()
    {
        const string text = "Personnummer 811218--9876 i CV.";

        var once = PersonnummerTextNormalizer.Normalize(text, PersonnummerGapProfile.ExtractedDocumentText);
        var twice = PersonnummerTextNormalizer.Normalize(once, PersonnummerGapProfile.ExtractedDocumentText);

        twice.ShouldBe(once);
    }

    [Fact]
    public void Normalize_SingleSeparatorContiguous_JoinsToIdempotentNoOp_StillFlagged()
    {
        // The {0,2} degenerate case: a single-separator contiguous "811218-9876" (no space) now
        // ALSO matches the bridge and joins to "8112189876" (drops the one separator). This is a
        // harmless no-op the Scan path already flags directly; assert it neither breaks detection
        // nor changes the outcome, and stays idempotent.
        const string text = "Personnummer 811218-9876 i CV.";

        var normalized = PersonnummerTextNormalizer.Normalize(text, PersonnummerGapProfile.ExtractedDocumentText);
        normalized.ShouldContain("8112189876");
        PersonnummerScanner.Scan(normalized)
            .ShouldHaveSingleItem()
            .Kind.ShouldBe(PersonnummerKind.Personnummer);
        PersonnummerTextNormalizer.Normalize(normalized, PersonnummerGapProfile.ExtractedDocumentText).ShouldBe(normalized);
    }

    // ===============================================================
    // #667 (STEG 1 pnr-scanner hardening): a FULLWIDTH-digit (\p{Nd}) personnummer gapped by a
    // space is bridged AND flagged too. Normalize's \d is already Unicode, so it joins the
    // fullwidth digit groups; TryParse then folds \p{Nd} to its 0-9 value. Fullwidth vectors are
    // built at runtime (source stays ASCII-only, project rule).
    // ===============================================================

    [Fact]
    public void Scan_FullwidthDigitsSpacedForm_FlaggedAfterNormalize()
    {
        var spaced = ToFullwidthDigits("811218") + " " + ToFullwidthDigits("9876");
        var text = $"Personnummer {spaced} i CV.";

        // Directly the contiguous scanner does not bridge the space (false negative).
        PersonnummerScanner.Scan(text).ShouldBeEmpty();

        // After Normalize joins the fullwidth digits, the SAME scanner flags the folded token.
        PersonnummerScanner.Scan(PersonnummerTextNormalizer.Normalize(text, PersonnummerGapProfile.ExtractedDocumentText))
            .ShouldHaveSingleItem()
            .Kind.ShouldBe(PersonnummerKind.Personnummer);
    }

    // Fullwidth (U+FF10-U+FF19) rendering of the ASCII digits in the input, built at runtime so
    // the source stays ASCII-only (project rule: no literal Unicode). Non-digits pass through.
    private static string ToFullwidthDigits(string s)
    {
        var chars = s.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] is >= '0' and <= '9')
                chars[i] = (char)(0xFF10 + (chars[i] - '0'));
        }

        return new string(chars);
    }
}
