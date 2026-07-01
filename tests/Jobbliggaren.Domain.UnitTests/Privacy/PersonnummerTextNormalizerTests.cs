using Jobbliggaren.Domain.Privacy;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Privacy;

// Fas 4 STEG 8 (F4-8, ADR 0074 Invariant 1) — the spaced-form personnummer
// call-site. F4-1 deferred the "spaced/OCR-gapped false-negative" here:
// PersonnummerTextNormalizer.Normalize bridges a (8-or-6 digits)(1–2 space
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
        var normalized = PersonnummerTextNormalizer.Normalize(text);
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

        var matches = PersonnummerScanner.Scan(PersonnummerTextNormalizer.Normalize(text));

        var match = matches.ShouldHaveSingleItem();
        match.Kind.ShouldBe(PersonnummerKind.Samordningsnummer);
    }

    [Fact]
    public void Scan_TabSeparatedValidPersonnummer_FlaggedAfterNormalize()
    {
        // A tab is the other OCR-gap shape the normalizer bridges ([\p{Zs}\t]{1,2}).
        const string spaced = "811218\t9876";
        var text = $"Pnr {spaced}.";

        PersonnummerScanner.Scan(text).ShouldBeEmpty();

        PersonnummerScanner.Scan(PersonnummerTextNormalizer.Normalize(text))
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
        PersonnummerScanner.Scan(PersonnummerTextNormalizer.Normalize(text))
            .ShouldHaveSingleItem()
            .Kind.ShouldBe(PersonnummerKind.Personnummer);
    }

    [Fact]
    public void Scan_NonBreakingSpaceSeparated12DigitPersonnummer_FlaggedAfterNormalize()
    {
        // The 12-digit full-century form gapped by U+00A0 (the exact #268 C1 vector).
        const string text = "19811218\u00A09876";

        PersonnummerScanner.Scan(text).ShouldBeEmpty();

        PersonnummerScanner.Scan(PersonnummerTextNormalizer.Normalize(text))
            .ShouldHaveSingleItem()
            .Kind.ShouldBe(PersonnummerKind.Personnummer);
    }

    [Fact]
    public void Scan_TwoCharGapNbspThenSpace_FlaggedAfterNormalize()
    {
        // A two-character gap mixing an NBSP and an ASCII space is within the {1,2}
        // bound and is bridged (digit-group separator immediately before a stray space).
        const string text = "Pnr 811218\u00A0 9876.";

        PersonnummerScanner.Scan(PersonnummerTextNormalizer.Normalize(text))
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

        var normalized = PersonnummerTextNormalizer.Normalize(text);

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

        var normalized = PersonnummerTextNormalizer.Normalize(text);

        normalized.ShouldContain("123456780000");
        PersonnummerScanner.Scan(normalized).ShouldBeEmpty();
    }

    [Fact]
    public void Scan_SpacedPhoneLikeFourPlusFour_NotBridged_NoMatch()
    {
        // A 4+4 split (e.g. part of a phone number) is NOT the 6/8 + 4 shape, so
        // the normalizer does not bridge it — and nothing is flagged.
        const string text = "Mobil 0701 234567 dagtid.";

        var normalized = PersonnummerTextNormalizer.Normalize(text);

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

        var normalized = PersonnummerTextNormalizer.Normalize(text);

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
        var once = PersonnummerTextNormalizer.Normalize(text);
        var twice = PersonnummerTextNormalizer.Normalize(once);

        twice.ShouldBe(once);
    }

    [Fact]
    public void Normalize_SameInput_ProducesSameOutput_Deterministic()
    {
        const string text = "Pnr 19811218 9876 och samordning 811278 9873 i CV.";

        var first = PersonnummerTextNormalizer.Normalize(text);
        var second = PersonnummerTextNormalizer.Normalize(text);

        second.ShouldBe(first);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \t  ")]
    [InlineData("Bara prosa utan personnummer.")]
    public void Normalize_TextWithoutBridgeableGap_ReturnsTextUnchanged(string text)
    {
        PersonnummerTextNormalizer.Normalize(text).ShouldBe(text);
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
        PersonnummerScanner.Scan(PersonnummerTextNormalizer.Normalize(text))
            .ShouldHaveSingleItem()
            .Kind.ShouldBe(PersonnummerKind.Personnummer);
    }

    [Fact]
    public void Scan_ZeroWidthSeparated12DigitPersonnummer_FlaggedAfterNormalize()
    {
        // The 12-digit full-century form gapped by U+200B (a PDF/DOCX extraction artefact).
        const string text = "19811218\u200B9876";

        PersonnummerScanner.Scan(text).ShouldBeEmpty();

        PersonnummerScanner.Scan(PersonnummerTextNormalizer.Normalize(text))
            .ShouldHaveSingleItem()
            .Kind.ShouldBe(PersonnummerKind.Personnummer);
    }

    [Fact]
    public void Scan_ZeroWidthThenNonBreakingSpaceGap_FlaggedAfterNormalize()
    {
        // A zero-width char adjacent to the NBSP digit-group separator: strip the ZW first,
        // then the {1,2} \p{Zs} bridge joins the digits.
        const string text = "Pnr 811218\u200B\u00A09876.";

        PersonnummerScanner.Scan(PersonnummerTextNormalizer.Normalize(text))
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

        var normalized = PersonnummerTextNormalizer.Normalize(text);

        normalized.ShouldContain("123456780000");
        PersonnummerScanner.Scan(normalized).ShouldBeEmpty();
    }

    [Fact]
    public void Normalize_ZeroWidthGapped_IsIdempotent()
    {
        const string text = "Personnummer 811218\u200B9876 i CV.";

        var once = PersonnummerTextNormalizer.Normalize(text);
        var twice = PersonnummerTextNormalizer.Normalize(once);

        twice.ShouldBe(once);
    }

    // #427 V3 (accepted residual): a 3+ VISIBLE-column gap is deliberately NOT bridged
    // (a wider window would risk bridging two unrelated numbers). The zero-width strip
    // does not change this — the {1,2} bound governs the visible \p{Zs}\t separators only.
    [Fact]
    public void Normalize_ThreeVisibleColumnGap_NotBridged()
    {
        const string text = "Pnr 811218   9876 slut.";

        var normalized = PersonnummerTextNormalizer.Normalize(text);

        normalized.ShouldNotContain("8112189876");
        PersonnummerScanner.Scan(normalized).ShouldBeEmpty();
    }

    // ===============================================================
    // #427 (2nd CTO ruling) — import path: R2 (a '-'/'+' separator ADJACENT to a space,
    // "811218- 9876" / "811218 -9876") is now bridged too. R1 (zero-width between two
    // spaces) is already handled by the \p{Cf} strip + the {1,2} bridge, asserted here
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
        PersonnummerScanner.Scan(PersonnummerTextNormalizer.Normalize(text))
            .ShouldHaveSingleItem()
            .Kind.ShouldBe(PersonnummerKind.Personnummer);
    }

    [Fact]
    public void Scan_TwoUnrelated_SeparatorAdjacentSpace_NotManufacturedAfterNormalize()
    {
        // The widened separator-adjacent-space bridge must NOT manufacture a valid pnr:
        // "12345678- 0000" joins to 123456780000 whose month "34" fails date sanity.
        const string text = "Referens 12345678- 0000 i systemet.";

        PersonnummerScanner.Scan(PersonnummerTextNormalizer.Normalize(text)).ShouldBeEmpty();
    }
}
