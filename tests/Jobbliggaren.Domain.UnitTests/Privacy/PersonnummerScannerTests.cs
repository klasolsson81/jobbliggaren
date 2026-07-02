using Jobbliggaren.Domain.Privacy;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Privacy;

// Fas 4 STEG 1 (F4-1) — free-text scanning over CV-like text.
// RED PHASE: PersonnummerScanner.Scan returns an empty list in the stub, so the
// detection tests here are expected to FAIL until GREEN. Naming:
// Method_Scenario_Expected. All vectors are SYNTHETIC test numbers.
public class PersonnummerScannerTests
{
    // ===============================================================
    // Group E — Scan: empty / no-match inputs
    // ===============================================================

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n  \r\n")]
    [InlineData("Erfaren utvecklare med fokus på .NET och React.")]
    public void Scan_TextWithoutPersonnummer_ReturnsEmptyList(string text)
    {
        var matches = PersonnummerScanner.Scan(text);

        matches.ShouldBeEmpty();
    }

    // ===============================================================
    // Group E — Scan: single detection with offset / length / kind
    // ===============================================================

    [Fact]
    public void Scan_ParagraphWithOnePersonnummer_ReturnsExactlyOneMatchWithCorrectSpanAndKind()
    {
        const string prefix = "Personnummer: ";
        const string pnr = "811218-9876";
        var text = $"{prefix}{pnr} (uppgift i CV).";

        var matches = PersonnummerScanner.Scan(text);

        var match = matches.ShouldHaveSingleItem();
        match.Kind.ShouldBe(PersonnummerKind.Personnummer);
        match.StartOffset.ShouldBe(prefix.Length);
        match.Length.ShouldBe(pnr.Length);
    }

    [Fact]
    public void Scan_ParagraphWithOneSamordningsnummer_ReturnsOneMatchWithSamordningsnummerKind()
    {
        const string prefix = "Samordningsnummer: ";
        const string samordning = "811278-9873";
        var text = $"{prefix}{samordning}.";

        var matches = PersonnummerScanner.Scan(text);

        var match = matches.ShouldHaveSingleItem();
        match.Kind.ShouldBe(PersonnummerKind.Samordningsnummer);
        match.StartOffset.ShouldBe(prefix.Length);
        match.Length.ShouldBe(samordning.Length);
    }

    // ===============================================================
    // Group E — Scan: multiple detections (pnr + samordningsnummer)
    // ===============================================================

    [Fact]
    public void Scan_TextWithPersonnummerAndSamordningsnummer_ReturnsTwoMatchesWithCorrectKindsInOrder()
    {
        const string first = "811218-9876"; // personnummer
        const string second = "811278-9873"; // samordningsnummer
        var text = $"Kandidat A {first} och kandidat B {second} i samma dokument.";

        var matches = PersonnummerScanner.Scan(text);

        matches.Count.ShouldBe(2);
        matches[0].Kind.ShouldBe(PersonnummerKind.Personnummer);
        matches[0].StartOffset.ShouldBe(text.IndexOf(first, StringComparison.Ordinal));
        matches[1].Kind.ShouldBe(PersonnummerKind.Samordningsnummer);
        matches[1].StartOffset.ShouldBe(text.IndexOf(second, StringComparison.Ordinal));
    }

    // ===============================================================
    // Group E — Scan: false-positive guards
    // ===============================================================

    [Theory]
    [InlineData("Ring mig på 070-123 45 67 om du har frågor.")] // phone
    [InlineData("Mobil: +46 70 123 45 67.")] // intl phone
    [InlineData("Växel 08-123456 mellan 9 och 17.")] // landline
    [InlineData("Senast uppdaterad 2026-06-14 av redaktören.")] // ISO date in prose
    [InlineData("Projektet löpte 2024-01-01 till 2026-06-14.")] // two ISO dates
    public void Scan_FalsePositiveCandidates_ReturnsEmptyList(string text)
    {
        var matches = PersonnummerScanner.Scan(text);

        matches.ShouldBeEmpty();
    }

    [Fact]
    public void Scan_PersonnummerEmbeddedInLongerDigitRun_DoesNotMatch_TokenBoundaryGuard()
    {
        // 8112189876 is Luhn-valid, but here it is a substring of a longer
        // undelimited digit run. A personnummer must be a delimited token, never
        // a substring — otherwise the guard over-flags arbitrary numbers.
        var text = "Referensnummer 99811218987612 i systemet.";

        var matches = PersonnummerScanner.Scan(text);

        matches.ShouldBeEmpty();
    }

    [Fact]
    public void Scan_PersonnummerNextToPhoneNumber_ReturnsOnlyThePersonnummer()
    {
        const string pnr = "811218-9876";
        var text = $"Kontakt: tel 070-123 45 67, personnummer {pnr}.";

        var matches = PersonnummerScanner.Scan(text);

        var match = matches.ShouldHaveSingleItem();
        match.Kind.ShouldBe(PersonnummerKind.Personnummer);
        match.StartOffset.ShouldBe(text.IndexOf(pnr, StringComparison.Ordinal));
        match.Length.ShouldBe(pnr.Length);
    }

    // ===============================================================
    // Group D (scanner surface) — matches never carry raw digits
    // ===============================================================

    [Fact]
    public void Scan_Match_MaskedContainsNoRawDigitsOfTheSource()
    {
        const string pnr = "811218-9876";
        var text = $"Personnummer {pnr} i CV.";

        var matches = PersonnummerScanner.Scan(text);

        var match = matches.ShouldHaveSingleItem();
        match.Masked.ShouldNotBeNull();
        match.Masked.ShouldNotContain("8112189876");
        match.Masked.ShouldNotContain(pnr);
    }

    // ===============================================================
    // #427 V1/V2 — ScanWithGaps: the gap-aware sibling of Scan for the REDACTION path.
    // Returns match spans into the ORIGINAL text INCLUDING the bridging gap, so a
    // redactor can mask IN PLACE. Contiguous parity with Scan; gated by the SAME
    // UNCHANGED Personnummer.TryParse date+Luhn authority (no over-flag). The 3+
    // visible-column gap stays a deliberately-unbridged accepted residual (V3).
    // ===============================================================

    [Fact]
    public void ScanWithGaps_ContiguousPersonnummer_ParityWithScan()
    {
        const string prefix = "Personnummer: ";
        const string pnr = "811218-9876";
        var text = $"{prefix}{pnr} i CV.";

        var match = PersonnummerScanner.ScanWithGaps(text).ShouldHaveSingleItem();

        match.Kind.ShouldBe(PersonnummerKind.Personnummer);
        match.StartOffset.ShouldBe(prefix.Length);
        match.Length.ShouldBe(pnr.Length);
        match.Masked.ShouldBe("******-****");
    }

    [Fact]
    public void ScanWithGaps_SpacedPersonnummer_SpanCoversTheGapInOriginalText()
    {
        const string prefix = "Personnummer ";
        const string gapped = "811218 9876"; // ASCII-space gap (11 chars)
        var text = $"{prefix}{gapped} i CV.";

        var match = PersonnummerScanner.ScanWithGaps(text).ShouldHaveSingleItem();

        // The span points into the ORIGINAL text and covers the gap, so masking
        // [StartOffset, StartOffset+Length) removes the raw digits in place — the
        // §12-load-bearing correctness (original spans, not an offset translation).
        match.StartOffset.ShouldBe(prefix.Length);
        match.Length.ShouldBe(gapped.Length);
        text.Substring(match.StartOffset, match.Length).ShouldBe(gapped);
        // Mask keeps the gap char and preserves length; no raw digit group survives.
        match.Masked.ShouldBe("****** ****");
        match.Masked.Length.ShouldBe(gapped.Length);
    }

    [Fact]
    public void ScanWithGaps_ZeroWidthGapped_MaskedKeepsGapChar_LengthPreserved()
    {
        const string gapped = "811218\u200B9876"; // U+200B ZERO WIDTH SPACE gap
        var text = $"Pnr {gapped}.";

        var match = PersonnummerScanner.ScanWithGaps(text).ShouldHaveSingleItem();

        match.Kind.ShouldBe(PersonnummerKind.Personnummer);
        match.Length.ShouldBe(gapped.Length);
        match.Masked.Length.ShouldBe(gapped.Length);
        match.Masked.ShouldNotContain("811218");
        match.Masked.ShouldNotContain("9876");
    }

    [Fact]
    public void ScanWithGaps_TwoUnrelatedNumbers_GappedButInvalid_NotFlagged()
    {
        // Bridged shape 123456780000 fails date sanity ("34" is no month) — the untouched
        // date+Luhn gate rejects it, so the wider candidate shape does not over-flag.
        const string text = "Referens 12345678 0000 i systemet.";

        PersonnummerScanner.ScanWithGaps(text).ShouldBeEmpty();
    }

    [Fact]
    public void ScanWithGaps_PersonnummerEmbeddedInLongerDigitRun_NotFlagged_TokenBoundary()
    {
        const string text = "Referensnummer 99811218987612 i systemet.";

        PersonnummerScanner.ScanWithGaps(text).ShouldBeEmpty();
    }

    [Fact]
    public void ScanWithGaps_ThreeVisibleColumnGap_NotBridged_NotFlagged()
    {
        // #427 V3 accepted residual: a 3+ visible-column gap is deliberately not bridged.
        const string text = "Pnr 811218   9876 i CV.";

        PersonnummerScanner.ScanWithGaps(text).ShouldBeEmpty();
    }

    [Fact]
    public void ScanWithGaps_GappedSamordningsnummer_ReturnsSamordningsnummerKind()
    {
        // The day+60 branch of TryParse through the gap-aware path: 811278 (day 18+60=78)
        // is a Luhn-valid samordningsnummer; spaced form must flag with the correct Kind.
        const string text = "Samordningsnummer 811278 9873 i CV.";

        PersonnummerScanner.ScanWithGaps(text)
            .ShouldHaveSingleItem()
            .Kind.ShouldBe(PersonnummerKind.Samordningsnummer);
    }

    [Fact]
    public void ScanWithGaps_ManyZeroWidthCharsInGap_MaskSpanUsesHeapPath_LengthPreserved()
    {
        // \p{Cf}* is unbounded, so a PDF/DOCX can emit a personnummer with many zero-width
        // chars in the gap. 27x U+200B → span length 37 (> MaskSpan's 32-char stack
        // threshold) → the heap-allocation branch runs. Proves the heap path masks the raw
        // digits and preserves length (every non-digit, incl. the zero-width chars, is kept).
        const string prefix = "Pnr ";
        var gap = new string('\u200B', 27);
        var gapped = $"811218{gap}9876"; // 6 + 27 + 4 = 37 chars
        var text = $"{prefix}{gapped} slut.";

        var match = PersonnummerScanner.ScanWithGaps(text).ShouldHaveSingleItem();

        match.Kind.ShouldBe(PersonnummerKind.Personnummer);
        match.Length.ShouldBe(gapped.Length); // 37 — regex spanned the whole gap
        match.Masked.Length.ShouldBe(gapped.Length); // heap-masked, length preserved
        match.Masked.ShouldNotContain("811218");
        match.Masked.ShouldNotContain("9876");
        match.Masked.Count(c => c == '\u200B').ShouldBe(27); // the zero-width chars are kept
        text.Substring(match.StartOffset, match.Length).ShouldBe(gapped);
    }

    // #427 — over-flag symmetry with Scan: the WIDER gap-aware shape (\d{8}|\d{6} + optional
    // gap + \d{4}) is still gated by the UNCHANGED date+Luhn authority, so phone numbers and
    // ISO dates that Scan rejects are rejected by ScanWithGaps too (no over-redaction).
    [Theory]
    [InlineData("Ring mig på 070-123 45 67 om du har frågor.")] // phone
    [InlineData("Mobil: +46 70 123 45 67.")] // intl phone
    [InlineData("Växel 08-123456 mellan 9 och 17.")] // landline
    [InlineData("Senast uppdaterad 2026-06-14 av redaktören.")] // ISO date in prose
    [InlineData("Projektet löpte 2024-01-01 till 2026-06-14.")] // two ISO dates
    public void ScanWithGaps_FalsePositiveCandidates_ReturnsEmptyList(string text)
    {
        PersonnummerScanner.ScanWithGaps(text).ShouldBeEmpty();
    }

    // ===============================================================
    // #427 (2nd CTO ruling) — two residual gap COMPOSITIONS closed on the redaction path:
    //   R1: zero-width \p{Cf} INTERLEAVED between two visible spaces (the import path
    //       already handled it via strip-first; the redaction path missed it because the
    //       {1,2} space run was broken by the \p{Cf}).
    //   R2: a '-'/'+' separator ADJACENT to a space ("811218- 9876" / "811218 -9876") —
    //       a realistic OCR rendering of a legitimate separator, missed by both paths.
    // Both lie INSIDE the already-bridged window (≤2 visible columns); the V3 accepted
    // residual (3+ visible columns) is UNCHANGED. Still gated by the unchanged date+Luhn.
    // Gap code points written as \u escapes.
    // ===============================================================

    [Theory]
    [InlineData("811218 \u200B 9876")] // R1: space, U+200B ZERO WIDTH SPACE, space
    [InlineData("811218- 9876")] // R2a: dash then space
    [InlineData("811218 -9876")] // R2b: space then dash
    public void ScanWithGaps_SeparatorOrInterleavedZeroWidthGap_IsFlagged(string gapped)
    {
        var text = $"Personnummer {gapped} i CV.";

        var match = PersonnummerScanner.ScanWithGaps(text).ShouldHaveSingleItem();

        match.Kind.ShouldBe(PersonnummerKind.Personnummer);
        // The span covers the whole gap in the original text, so the redactor masks in place.
        text.Substring(match.StartOffset, match.Length).ShouldBe(gapped);
        match.Masked.Length.ShouldBe(gapped.Length);
        match.Masked.ShouldNotContain("811218");
        match.Masked.ShouldNotContain("9876");
    }

    [Theory]
    [InlineData("Referens 12345678- 0000 i systemet.")] // R2a: dash then space
    [InlineData("Referens 12345678 -0000 i systemet.")] // R2b: space then dash
    public void ScanWithGaps_TwoUnrelatedNumbers_SeparatorAdjacentSpace_NotManufactured(string text)
    {
        // The widened separator-adjacent-space shape must NOT over-flag: "12345678- 0000"
        // joins to 123456780000, whose month "34" fails date sanity → the unchanged gate rejects it.
        PersonnummerScanner.ScanWithGaps(text).ShouldBeEmpty();
    }

    [Fact]
    public void ScanWithGaps_DoubleSeparatorAroundSpace_IsMasked_SupersedesAcceptedResidual()
    {
        // #498 (SUPERSEDES #427's accepted-residual ruling; see PR body / review report):
        // "811218- -9876" (a '-'/'+' separator adjacent to BOTH sides of the space run) IS
        // flagged by the import guard (Normalize drops both separators via $1$2 to "8112189876"
        // then Scan flags), but the redaction path previously left it, leaking the raw digits
        // into the UNENCRYPTED parsed_resumes.source_file_name column and CV evidence. Under the
        // digit-only strip (Approach A / Q2 CTO) the validation token is the digits alone, so the
        // UNCHANGED TryParse date+Luhn validates it and the redactor masks the ORIGINAL span in
        // place. Redaction is now a true SUPERSET of the flag path for this shape.
        const string text = "Pnr 811218- -9876 i CV.";

        var match = PersonnummerScanner.ScanWithGaps(text).ShouldHaveSingleItem();

        match.Kind.ShouldBe(PersonnummerKind.Personnummer);
        text.Substring(match.StartOffset, match.Length).ShouldBe("811218- -9876");
        match.Masked.Length.ShouldBe("811218- -9876".Length); // length-preserving, masked in place
        match.Masked.Any(char.IsAsciiDigit).ShouldBeFalse(); // no raw digit survives
    }

    [Fact]
    public void ScanWithGaps_DoubleSeparatorNoSpace_NotFlagged_AndNeverThrows()
    {
        // "12345678--0000" (two adjacent separators, no space) strips (digit-only, Approach A)
        // to the joined "123456780000", whose month field "56" fails date sanity, so the
        // UNCHANGED TryParse rejects it (not for a two-separator token: the strip no longer keeps
        // separators, but because it is not a valid date). Buffer regression guard: digit-only
        // tokens are <= 12 chars, well within the stackalloc, and never throw.
        const string text = "Referens 12345678--0000 i systemet.";

        var act = () => PersonnummerScanner.ScanWithGaps(text);

        act.ShouldNotThrow().ShouldBeEmpty();
    }

    [Fact]
    public void ScanWithGaps_SeparatorFlankedByTwoSpaces_NotBridged_V3BoundHolds()
    {
        // A '-'/'+' flanked by a space on BOTH sides ("811218 - 9876") is a 3-visible-column
        // gap: the widening did not open it (the {0,2} space bound governs each side), so the
        // V3 accepted residual still holds and the form is not flagged.
        const string text = "Pnr 811218 - 9876 i CV.";

        PersonnummerScanner.ScanWithGaps(text).ShouldBeEmpty();
    }

    // ===============================================================
    // #497 (ADR 0074 Invariant 1): Unicode-dash separators, EN DASH (U+2013),
    // NON-BREAKING HYPHEN (U+2011, \p{Pd}), MINUS SIGN (U+2212, \p{Sm}), are all
    // Luhn-valid personnummer separators the product itself renders/emits, but were an
    // end-to-end false negative under the hardcoded ASCII [-+] classes. The widened
    // separator SHAPE class is now shared by Scan (contiguous flag path) and ScanWithGaps
    // (redaction path); TryParse's date+Luhn authority is unchanged. Separators as \u escapes.
    // ===============================================================

    [Theory]
    [InlineData("811218\u20139876")] // U+2013 EN DASH
    [InlineData("811218\u20119876")] // U+2011 NON-BREAKING HYPHEN
    [InlineData("811218\u22129876")] // U+2212 MINUS SIGN
    [InlineData("811218\u20109876")] // U+2010 HYPHEN (another \p{Pd}: pins the class is a category)
    [InlineData("811218\u20149876")] // U+2014 EM DASH (another \p{Pd})
    public void Scan_UnicodeDashPersonnummer_IsFlagged(string pnr)
    {
        var text = $"Personnummer {pnr} i CV.";

        var match = PersonnummerScanner.Scan(text).ShouldHaveSingleItem();

        match.Kind.ShouldBe(PersonnummerKind.Personnummer);
        match.StartOffset.ShouldBe(text.IndexOf(pnr, StringComparison.Ordinal));
        match.Length.ShouldBe(pnr.Length);
    }

    [Theory]
    [InlineData("811218\u20139876")] // U+2013 EN DASH
    [InlineData("811218\u20119876")] // U+2011 NON-BREAKING HYPHEN
    [InlineData("811218\u22129876")] // U+2212 MINUS SIGN
    [InlineData("811218\u20109876")] // U+2010 HYPHEN (another \p{Pd}: pins the class is a category)
    [InlineData("811218\u20149876")] // U+2014 EM DASH (another \p{Pd})
    public void ScanWithGaps_UnicodeDashPersonnummer_IsMasked(string pnr)
    {
        var text = $"Personnummer {pnr} i CV.";

        var match = PersonnummerScanner.ScanWithGaps(text).ShouldHaveSingleItem();

        match.Kind.ShouldBe(PersonnummerKind.Personnummer);
        // Span covers the whole token in the ORIGINAL text so the redactor masks in place.
        text.Substring(match.StartOffset, match.Length).ShouldBe(pnr);
        match.Masked.Length.ShouldBe(pnr.Length);
        match.Masked.Any(char.IsAsciiDigit).ShouldBeFalse(); // no raw digit survives
    }

    // ===============================================================
    // #498(a) (ADR 0074 Invariant 1): a zero-width \p{Cf} character INSIDE a digit group
    // (not just in the gap). The import flag path strips \p{Cf} GLOBALLY (Normalize) and so
    // flags it, but the redaction GapAwareCandidateRegex required CONTINUOUS digit groups and
    // missed it, leaving the raw digits at rest in the UNENCRYPTED source_file_name column
    // (falsifies #465's superset invariant, whose corpus only placed \p{Cf} in the GAP). The
    // gap-aware digit groups now tolerate interleaved \p{Cf} ((?:\d\p{Cf}*){n}). \u escapes.
    // ===============================================================

    [Theory]
    [InlineData("8112\u200B18-9876")] // U+200B ZERO WIDTH SPACE inside the leading 6-digit group
    [InlineData("811218-98\u200B76")] // U+200B inside the trailing 4-digit group
    [InlineData("81\uFEFF1218-9876")] // U+FEFF ZERO WIDTH NO-BREAK SPACE inside the leading group
    public void ScanWithGaps_ZeroWidthInsideDigitGroup_IsMasked(string pnr)
    {
        var text = $"Personnummer {pnr} i CV.";

        var match = PersonnummerScanner.ScanWithGaps(text).ShouldHaveSingleItem();

        match.Kind.ShouldBe(PersonnummerKind.Personnummer);
        text.Substring(match.StartOffset, match.Length).ShouldBe(pnr);
        match.Masked.Length.ShouldBe(pnr.Length);
        match.Masked.Any(char.IsAsciiDigit).ShouldBeFalse(); // every raw digit masked, zero-width kept
    }

    [Fact]
    public void ScanWithGaps_TwelveDigitUnicodeDashPersonnummer_IsMasked()
    {
        // #497 through the 12-digit full-century form (a DISTINCT regex alternative than the
        // 10-digit one), on the redaction path - the earlier #497 vectors only cover 10-digit.
        const string pnr = "19811218\u20139876";
        var text = $"Kandidat {pnr} i CV.";

        var match = PersonnummerScanner.ScanWithGaps(text).ShouldHaveSingleItem();

        match.Kind.ShouldBe(PersonnummerKind.Personnummer);
        text.Substring(match.StartOffset, match.Length).ShouldBe(pnr);
        match.Masked.Any(char.IsAsciiDigit).ShouldBeFalse();
    }

    [Fact]
    public void UnicodeDashSamordningsnummer_IsFlaggedOnBothPaths_WithSamordningsnummerKind()
    {
        // #497 through the samordningsnummer (day+60) branch: 811278-9873 with an EN DASH,
        // on BOTH the contiguous flag path (Scan) and the redaction path (ScanWithGaps).
        const string text = "Samordningsnummer 811278\u20139873 i CV.";

        PersonnummerScanner.Scan(text)
            .ShouldHaveSingleItem().Kind.ShouldBe(PersonnummerKind.Samordningsnummer);
        PersonnummerScanner.ScanWithGaps(text)
            .ShouldHaveSingleItem().Kind.ShouldBe(PersonnummerKind.Samordningsnummer);
    }

    [Fact]
    public void ScanWithGaps_ZeroWidthAdjacentToSeparator_IsMasked()
    {
        // #498 regression guard (review pitfall 5): a \p{Cf} riding IMMEDIATELY AFTER the
        // separator ("811218-<ZWSP>9876") must still be masked. This pins the \p{Cf}* that
        // trails the separator group in GapAwareCandidateRegex - a mutation dropping it would
        // pass every other vector while leaking this PDF/DOCX-realistic shape at rest.
        const string pnr = "811218-\u200B9876";
        var text = $"Personnummer {pnr} i CV.";

        var match = PersonnummerScanner.ScanWithGaps(text).ShouldHaveSingleItem();

        match.Kind.ShouldBe(PersonnummerKind.Personnummer);
        text.Substring(match.StartOffset, match.Length).ShouldBe(pnr);
        match.Masked.Any(char.IsAsciiDigit).ShouldBeFalse();
    }
}
