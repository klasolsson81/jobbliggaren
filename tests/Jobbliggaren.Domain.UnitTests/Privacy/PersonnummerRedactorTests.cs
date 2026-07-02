using Jobbliggaren.Domain.Privacy;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Privacy;

/// <summary>
/// Fas 4 hardening-STEG (CTO-bound, <c>docs/reviews/2026-06-16-f4-hardening-pnr-evidence-cto.md</c>) —
/// the new <see cref="PersonnummerRedactor"/> Domain.Privacy primitive. NO AI/LLM (ADR 0071).
///
/// <para>RED PHASE: <c>PersonnummerRedactor</c> does not exist yet. These tests are expected to
/// FAIL to compile/run until CC ships <c>public static class PersonnummerRedactor</c> with
/// <c>public static string Redact(string text)</c> in <c>src/Jobbliggaren.Domain/Privacy/</c>.
/// CC writes production to green afterward — the test-writer does NOT implement production.</para>
///
/// <para>Contract under test (derived from the existing <see cref="PersonnummerScanner"/> +
/// <see cref="PersonnummerMatch"/>, never guessed): <c>Redact</c> scans via
/// <see cref="PersonnummerScanner.ScanWithGaps"/> and replaces each matched span (right-to-left by
/// <see cref="PersonnummerMatch.StartOffset"/> to preserve earlier offsets) with that match's
/// <see cref="PersonnummerMatch.Masked"/> form. <c>Masked</c> replaces every ASCII digit with
/// '*' and keeps the separator (e.g. "811218-9876" → "******-****").</para>
///
/// <para>Established Luhn-valid test vectors (parity <see cref="PersonnummerScannerTests"/> /
/// <c>PersonnummerScanOutcomeTests</c>): <c>811218-9876</c> (personnummer) and
/// <c>811278-9873</c> (samordningsnummer). All vectors are SYNTHETIC test numbers.</para>
/// </summary>
public class PersonnummerRedactorTests
{
    private const string Personnummer = "811218-9876";
    private const string Samordningsnummer = "811278-9873";

    // The mask the real Personnummer.MaskSpan produces for either delimited 10-digit token:
    // every ASCII digit → '*', the '-' separator preserved, length preserved.
    // Derived from the scanner's own Masked output below — NOT hardcoded blindly.
    private const string Mask = "******-****";

    // The expected mask is whatever the REAL scanner emits — assert our constant matches it so
    // the rest of the file can read cleanly while staying anti-stale (no guessed mask string).
    private static string MaskOf(string pnr)
    {
        var match = PersonnummerScanner.Scan(pnr).ShouldHaveSingleItem();
        return match.Masked;
    }

    [Fact]
    public void Mask_MatchesTheRealScannerMaskedForm_ForBothVectors()
    {
        // Anti-stale anchor: prove the constants this file reasons about are the scanner's
        // actual Masked output, so a future Masked change makes THIS fail first (loud), not the
        // redaction assertions silently.
        MaskOf(Personnummer).ShouldBe(Mask);
        MaskOf(Samordningsnummer).ShouldBe(Mask);
    }

    // ===============================================================
    // Group R — Redact: a single personnummer is masked, surroundings preserved
    // ===============================================================

    [Fact]
    public void Redact_TextWithOnePersonnummer_RemovesRawDigitsAndInsertsMask()
    {
        const string prefix = "Personnummer: ";
        const string suffix = " (uppgift i CV).";
        var text = $"{prefix}{Personnummer}{suffix}";

        var redacted = PersonnummerRedactor.Redact(text);

        redacted.ShouldNotContain(Personnummer);
        redacted.ShouldNotContain("8112189876"); // the raw digits, separator-free
        redacted.ShouldContain(Mask);
        // Surrounding (non-PII) text is preserved verbatim.
        redacted.ShouldStartWith(prefix);
        redacted.ShouldEndWith(suffix);
        redacted.ShouldBe($"{prefix}{Mask}{suffix}");
    }

    // ===============================================================
    // Group R — Redact: a samordningsnummer is redacted likewise
    // ===============================================================

    [Fact]
    public void Redact_TextWithOneSamordningsnummer_RemovesRawDigitsAndInsertsMask()
    {
        const string prefix = "Samordningsnummer: ";
        var text = $"{prefix}{Samordningsnummer}.";

        var redacted = PersonnummerRedactor.Redact(text);

        redacted.ShouldNotContain(Samordningsnummer);
        redacted.ShouldNotContain("8112789873");
        redacted.ShouldContain(Mask);
        redacted.ShouldBe($"{prefix}{Mask}.");
    }

    // ===============================================================
    // Group R — Redact: multiple personnummer in one string (right-to-left offset preservation)
    // ===============================================================

    [Fact]
    public void Redact_TextWithTwoNumbers_RedactsAll_AndPreservesSurroundingText()
    {
        // Two delimited tokens with prose between them. Proves right-to-left replacement: the
        // first (earlier-offset) span must still be masked correctly after the second is replaced.
        const string first = Personnummer;       // earlier offset
        const string second = Samordningsnummer; // later offset
        var text = $"Kandidat A {first} och kandidat B {second} i samma dokument.";

        var redacted = PersonnummerRedactor.Redact(text);

        redacted.ShouldNotContain(first);
        redacted.ShouldNotContain(second);
        redacted.ShouldNotContain("8112189876");
        redacted.ShouldNotContain("8112789873");
        // Surrounding prose intact and BOTH tokens replaced by the mask.
        redacted.ShouldBe($"Kandidat A {Mask} och kandidat B {Mask} i samma dokument.");
    }

    [Fact]
    public void Redact_SamePersonnummerTwice_RedactsBothOccurrences()
    {
        var text = $"{Personnummer} och igen {Personnummer}.";

        var redacted = PersonnummerRedactor.Redact(text);

        redacted.ShouldNotContain(Personnummer);
        redacted.ShouldBe($"{Mask} och igen {Mask}.");
    }

    // ===============================================================
    // Group R — Redact: clean / empty inputs are returned unchanged, never throw
    // ===============================================================

    [Fact]
    public void Redact_CleanTextWithoutPersonnummer_ReturnsItUnchanged()
    {
        const string text = "Erfaren utvecklare med fokus på .NET och React. Telefon 070-123 45 67.";

        var redacted = PersonnummerRedactor.Redact(text);

        redacted.ShouldBe(text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n  \r\n")]
    public void Redact_EmptyOrWhitespace_ReturnsInputUnchanged_NeverThrows(string text)
    {
        var act = () => PersonnummerRedactor.Redact(text);

        var redacted = act.ShouldNotThrow();
        redacted.ShouldBe(text);
    }

    // ===============================================================
    // Group R — No over-redaction: only real scanner matches are touched
    // ===============================================================

    [Fact]
    public void Redact_LuhnInvalidDigitRunTheScannerDoesNotMatch_IsNotRedacted()
    {
        // Parity with the scanner: redaction touches ONLY real scanner matches. An ISO date and
        // a phone number are not personnummer (the scanner returns no match) → unchanged.
        const string text = "Senast uppdaterad 2026-06-14. Ring 070-123 45 67.";

        // Guard: the scanner itself finds nothing here (anti-stale — if the scanner ever started
        // matching these, this test would need revisiting, and that surfaces loudly).
        PersonnummerScanner.Scan(text).ShouldBeEmpty();

        var redacted = PersonnummerRedactor.Redact(text);

        redacted.ShouldBe(text);
        redacted.ShouldNotContain("*");
    }

    [Fact]
    public void Redact_PersonnummerEmbeddedInLongerDigitRun_IsNotRedacted_TokenBoundaryParity()
    {
        // 8112189876 is Luhn-valid, but here it is a substring of a longer undelimited digit
        // run — the scanner's token-boundary guard does NOT match it, so redaction must not
        // touch it (no over-redaction of arbitrary digit runs).
        const string text = "Referensnummer 99811218987612 i systemet.";

        PersonnummerScanner.Scan(text).ShouldBeEmpty();

        var redacted = PersonnummerRedactor.Redact(text);

        redacted.ShouldBe(text);
    }

    // ===============================================================
    // Group R — The result NEVER contains the raw input personnummer (the load-bearing invariant)
    // ===============================================================

    [Fact]
    public void Redact_Result_NeverContainsTheRawInputPersonnummerSubstring()
    {
        // Mixed content: prose, a phone number, AND a real personnummer. The single non-negotiable
        // post-condition (ADR 0074 Inv. 1): the raw pnr is GONE from the output.
        var text =
            $"Kontakt: tel 070-123 45 67, personnummer {Personnummer}. " +
            $"Tidigare angivet {Samordningsnummer} också.";

        var redacted = PersonnummerRedactor.Redact(text);

        redacted.ShouldNotContain(Personnummer);
        redacted.ShouldNotContain(Samordningsnummer);
        redacted.ShouldNotContain("8112189876");
        redacted.ShouldNotContain("8112789873");
        // The phone number (not a pnr) is left intact — proves precision, not blanket digit-nuking.
        redacted.ShouldContain("070-123 45 67");
    }

    // ===============================================================
    // #427 V1 (ADR 0074 Invariant 1): a spaced/OCR-gapped or zero-width-gapped
    // personnummer INSIDE a single quoted snippet is now masked too. The redactor uses
    // the gap-aware scan (PersonnummerScanner.ScanWithGaps), whose spans cover the
    // ORIGINAL text INCLUDING the bridging gap, so the raw digit groups are stripped in
    // place. Before the fix the redactor scanned the un-normalized text with the
    // contiguous scanner and left these forms un-masked — a residual PII leak on the
    // review/improvement evidence surface. Gap separators are written as \u escapes so
    // each code point is explicit; all vectors are SYNTHETIC Luhn-valid test numbers.
    // ===============================================================

    [Theory]
    [InlineData("811218 9876")] // U+0020 ASCII SPACE
    [InlineData("811218\u00A09876")] // U+00A0 NO-BREAK SPACE (this app's digit-group separator)
    [InlineData("811218\u202F9876")] // U+202F NARROW NO-BREAK SPACE
    [InlineData("811218\u20099876")] // U+2009 THIN SPACE
    [InlineData("811218\u200B9876")] // U+200B ZERO WIDTH SPACE (\p{Cf})
    [InlineData("811218\uFEFF9876")] // U+FEFF ZERO WIDTH NO-BREAK SPACE (\p{Cf})
    public void Redact_GappedPersonnummerInsideQuote_MasksTheRawDigitsInPlace(string gapped)
    {
        const string prefix = "Personnummer: ";
        const string suffix = " (uppgift i CV).";
        var text = $"{prefix}{gapped}{suffix}";

        var redacted = PersonnummerRedactor.Redact(text);

        // The load-bearing invariant: BOTH raw digit groups are gone (no separator-free
        // form appears in the gapped source, so the digit groups are the honest assertion).
        redacted.ShouldNotContain("811218");
        redacted.ShouldNotContain("9876");
        // Masking happened; the gap character is kept so the span length is preserved.
        redacted.ShouldContain("*");
        redacted.Length.ShouldBe(text.Length);
        // Surrounding (non-PII) text preserved verbatim.
        redacted.ShouldStartWith(prefix);
        redacted.ShouldEndWith(suffix);
    }

    [Fact]
    public void Redact_ZeroWidthGapped12DigitPersonnummer_MasksTheRawDigits()
    {
        // The full-century 12-digit form gapped by a ZERO WIDTH SPACE (a PDF/DOCX artefact).
        const string text = "Kandidat 19811218\u200B9876 i dokumentet.";

        var redacted = PersonnummerRedactor.Redact(text);

        redacted.ShouldNotContain("19811218");
        redacted.ShouldNotContain("9876");
        redacted.ShouldContain("*");
    }

    [Fact]
    public void Redact_SpacedSamordningsnummerInsideQuote_MasksTheRawDigits()
    {
        // 811278-9873 is a Luhn-valid samordningsnummer (day 18+60=78); spaced form.
        const string text = "Samordningsnummer 811278 9873 i CV.";

        var redacted = PersonnummerRedactor.Redact(text);

        redacted.ShouldNotContain("811278");
        redacted.ShouldNotContain("9873");
        redacted.ShouldContain("*");
    }

    // ===============================================================
    // #427 V1/V3 — no over-redaction: the wider gap-aware shape is still gated by the
    // UNCHANGED date+Luhn authority, and the 3+ visible-column gap is deliberately not
    // bridged (accepted residual). Both forms are left untouched by the redactor.
    // ===============================================================

    [Fact]
    public void Redact_TwoUnrelatedAdjacentNumbers_GappedButNotAPersonnummer_LeftUnchanged()
    {
        // "12345678 0000" bridges to 123456780000, whose month field "34" fails date
        // sanity, so TryParse rejects it — the redactor must not touch it.
        const string text = "Referens 12345678 0000 i systemet.";

        var redacted = PersonnummerRedactor.Redact(text);

        redacted.ShouldBe(text);
    }

    [Fact]
    public void Redact_ThreeVisibleColumnGap_NotBridged_LeftUnchanged()
    {
        // A 3-visible-space gap is deliberately NOT bridged (#427 V3 accepted residual —
        // a wider window would risk bridging two unrelated numbers). The import-time guard
        // still flags the CV; here the redactor leaves the form as-is.
        const string text = "Pnr 811218   9876 i CV.";

        var redacted = PersonnummerRedactor.Redact(text);

        redacted.ShouldBe(text);
    }

    [Fact]
    public void Redact_TwoSpacedNumbersInOneString_MasksBothInPlace_RightToLeftOffsetsHold()
    {
        // Two spaced-gapped numbers (pnr + samordningsnummer). The right-to-left masking
        // must keep the EARLIER gap span's offset valid after the LATER gap span is
        // replaced — the reason OrderByDescending exists, until now only tested on
        // contiguous tokens. The realistic CV form: two people, both spaced.
        const string first = "811218 9876"; // spaced personnummer, earlier offset
        const string second = "811278 9873"; // spaced samordningsnummer, later offset
        var text = $"Kandidat A {first} och kandidat B {second} i CV.";

        var redacted = PersonnummerRedactor.Redact(text);

        redacted.ShouldNotContain("811218");
        redacted.ShouldNotContain("9876");
        redacted.ShouldNotContain("811278");
        redacted.ShouldNotContain("9873");
        redacted.Length.ShouldBe(text.Length); // length-preserving in-place masking
        redacted.ShouldStartWith("Kandidat A ");
        redacted.ShouldEndWith(" i CV.");
        redacted.ShouldContain("kandidat B"); // prose between the two intact
    }

    [Fact]
    public void Redact_ContiguousAndSpacedInSameText_MasksBoth()
    {
        // ScanWithGaps finds BOTH the contiguous and the spaced form in one pass (the
        // optional separator group is empty for the contiguous one). The redactor masks
        // both; a mixed input is the realistic CV form.
        const string contiguous = "811218-9876"; // contiguous
        const string spaced = "811278 9873";     // spaced
        var text = $"Uppgift 1: {contiguous}. Uppgift 2: {spaced}.";

        var redacted = PersonnummerRedactor.Redact(text);

        redacted.ShouldNotContain("811218");
        redacted.ShouldNotContain("9876");
        redacted.ShouldNotContain("811278");
        redacted.ShouldNotContain("9873");
        redacted.ShouldContain("*");
    }

    [Fact]
    public void Redact_GappedPersonnummerAtStringStart_MasksIt_OffsetZeroEdge()
    {
        // A gapped personnummer at absolute offset 0 hardens the StartOffset == 0 edge of
        // the Remove/Insert masking (all other gapped tests carry a prefix).
        const string text = "811218 9876 är ett testnummer.";

        var redacted = PersonnummerRedactor.Redact(text);

        redacted.ShouldNotContain("811218");
        redacted.ShouldNotContain("9876");
        redacted.ShouldStartWith("****** ****");
        redacted.ShouldEndWith(" är ett testnummer.");
    }

    // ===============================================================
    // #427 (2nd CTO ruling) — R1 (zero-width \p{Cf} interleaved between two visible
    // spaces) and R2 (a '-'/'+' separator adjacent to a space) are now masked by the
    // redactor too. Both lie INSIDE the already-bridged window (≤2 visible columns); the
    // V3 accepted residual (3+ visible columns) is unchanged. Gap points as \u escapes.
    // ===============================================================

    [Theory]
    [InlineData("811218 \u200B 9876")] // R1: space, U+200B ZERO WIDTH SPACE, space
    [InlineData("811218- 9876")] // R2a: dash then space
    [InlineData("811218 -9876")] // R2b: space then dash
    public void Redact_SeparatorOrInterleavedZeroWidthGap_MasksTheRawDigits(string gapped)
    {
        var text = $"Personnummer {gapped} i CV.";

        var redacted = PersonnummerRedactor.Redact(text);

        redacted.ShouldNotContain("811218");
        redacted.ShouldNotContain("9876");
        redacted.ShouldContain("*");
        redacted.Length.ShouldBe(text.Length); // length-preserving in-place masking
    }

    // ===============================================================
    // #465 (senior-cto-advisor, docs/reviews/2026-07-01-465-srcfilename-pnr-cto.md):
    // superset-equivalence GUARD. The import guard (#426) FLAGS a filename via the
    // Scan(Normalize(x)) path (SpacedCandidateRegex bridges up-to-2 visible-space gaps and
    // strips \p{Cf}); the redactor (this class) MASKS via ScanWithGaps. Both gate on the SAME
    // unchanged Personnummer.TryParse and share the gap-class rule, so ScanWithGaps is a
    // SUPERSET of Scan(Normalize(x)). This test pins the ONLY direction that protects the
    // at-rest plaintext: every form the #426 flag detects, the redactor also masks
    // (Scan(Normalize(x)).Count > 0  implies  Redact(x) != x). If a future divergence let the
    // flag fire while the redactor left the plaintext, THIS fails first (loud). The reverse
    // implication is deliberately NOT asserted: the redactor's legitimately-wider coverage
    // (e.g. an OCR gap the older flag missed) is desirable, not a defect.
    // ===============================================================

    [Theory]
    [InlineData("811218-9876")] // contiguous personnummer
    [InlineData("811278-9873")] // contiguous samordningsnummer
    [InlineData("19811218-9876")] // full-century 12-digit
    [InlineData("811218 9876")] // U+0020 spaced
    [InlineData("811218\u00A09876")] // U+00A0 NO-BREAK SPACE (this app's digit-group separator)
    [InlineData("811218\u202F9876")] // U+202F NARROW NO-BREAK SPACE
    [InlineData("811218\u20099876")] // U+2009 THIN SPACE
    [InlineData("811218\u200B9876")] // U+200B ZERO WIDTH SPACE (\p{Cf}, stripped by Normalize)
    [InlineData("811218\uFEFF9876")] // U+FEFF ZERO WIDTH NO-BREAK SPACE (\p{Cf})
    [InlineData("811218- 9876")] // R2a dash-space
    [InlineData("811218 -9876")] // R2b space-dash
    [InlineData("811218 \u200B 9876")] // R1 interleaved zero-width between two spaces
    [InlineData("CV_811218-9876.pdf")] // filename-wrapped (the #465 motivating shape)
    // #498(a): \p{Cf} INSIDE a digit group (not just in the gap), the corpus gap the #465
    // superset invariant missed. Flag path strips \p{Cf} globally and flags; redaction must
    // mask too (widened gap-aware digit groups).
    [InlineData("8112\u200B18-9876")] // U+200B inside the leading 6-digit group
    [InlineData("811218-98\u200B76")] // U+200B inside the trailing 4-digit group
    // #498(b): dash-space-dash, supersedes #427's accepted residual (leaked at rest).
    [InlineData("811218- -9876")] // '-'/'+' adjacent to BOTH sides of the space run
    // #497: Unicode-dash separators (contiguous + adjacent-space).
    [InlineData("811218\u20139876")] // U+2013 EN DASH
    [InlineData("811218\u20119876")] // U+2011 NON-BREAKING HYPHEN
    [InlineData("811218\u22129876")] // U+2212 MINUS SIGN
    [InlineData("811218\u2013 9876")] // U+2013 EN DASH adjacent to a space
    public void Redact_MasksEveryFormThe426FlagPathDetects_SupersetGuard(string form)
    {
        // Precondition: the #426 import-flag path detects a personnummer in this form. If a
        // vector ever stops satisfying this, the corpus (not the invariant) is stale; fix here.
        var flagged = PersonnummerScanner.Scan(PersonnummerTextNormalizer.Normalize(form)).Count > 0;
        flagged.ShouldBeTrue(
            $"corpus vector should be detectable by the #426 flag path: {form}");

        // The load-bearing invariant: what #426 flags, the redactor masks, so the plaintext
        // is never left at rest when the flag fired.
        PersonnummerRedactor.Redact(form).ShouldNotBe(form);
    }

    // ===============================================================
    // #497 / #498 direct redaction: the widened forms are masked at rest (the load-bearing
    // invariant for the UNENCRYPTED source_file_name column + CV review/improve evidence).
    // Every ASCII digit of the pnr is masked; the separator/gap/zero-width chars are kept so
    // the span length is preserved and maps 1:1 onto the original. \u escapes.
    // ===============================================================

    [Theory]
    [InlineData("811218\u20139876")] // #497 EN DASH
    [InlineData("811218\u20119876")] // #497 NON-BREAKING HYPHEN
    [InlineData("811218\u22129876")] // #497 MINUS SIGN
    [InlineData("811218\u2013 9876")] // #497 en-dash adjacent to a space
    [InlineData("8112\u200B18-9876")] // #498a \p{Cf} inside the leading digit group
    [InlineData("811218-98\u200B76")] // #498a \p{Cf} inside the trailing digit group
    [InlineData("811218- -9876")] // #498b dash-space-dash
    public void Redact_UnicodeDashOrZeroWidthInGroupOrDoubleSep_MasksEveryRawDigit(string form)
    {
        var text = $"Personnummer: {form} (uppgift i CV).";

        var redacted = PersonnummerRedactor.Redact(text);

        redacted.ShouldNotBe(text);
        // No ASCII digit of the pnr survives anywhere (the only digits in the text are the pnr).
        redacted.Any(char.IsAsciiDigit).ShouldBeFalse();
        redacted.ShouldContain("*");
        // Length preserved (in-place, length-preserving masking).
        redacted.Length.ShouldBe(text.Length);
    }
}
