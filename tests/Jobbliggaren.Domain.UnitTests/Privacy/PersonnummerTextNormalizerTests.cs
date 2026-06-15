using Jobbliggaren.Domain.Privacy;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Privacy;

// Fas 4 STEG 8 (F4-8, ADR 0074 Invariant 1) — the spaced-form personnummer
// call-site. F4-1 deferred the "spaced/OCR-gapped false-negative" here:
// PersonnummerTextNormalizer.Normalize bridges a (8-or-6 digits)(1–2 spaces/tabs)
// (4 digits) gap on a TRANSIENT scan-copy so the UNCHANGED context-free
// PersonnummerScanner.Scan can then FLAG the form. The safety stays in the
// untouched validation layer (Personnummer.TryParse date+Luhn), so bridging can
// never manufacture a VALID false positive out of two unrelated numbers.
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
        // A tab is the other OCR-gap shape the normalizer bridges ([ \t]{1,2}).
        const string spaced = "811218\t9876";
        var text = $"Pnr {spaced}.";

        PersonnummerScanner.Scan(text).ShouldBeEmpty();

        PersonnummerScanner.Scan(PersonnummerTextNormalizer.Normalize(text))
            .ShouldHaveSingleItem()
            .Kind.ShouldBe(PersonnummerKind.Personnummer);
    }

    // ===============================================================
    // No false positive: two UNRELATED adjacent numbers that do NOT together
    // form a date+Luhn-valid personnummer are NOT manufactured into a match —
    // because TryParse's date+Luhn gate is untouched.
    // ===============================================================

    [Fact]
    public void Scan_TwoUnrelatedAdjacentNumbers_NotManufacturedIntoMatch_AfterNormalize()
    {
        // 8-digit run + space + 4-digit run that, joined, fails date-sanity/Luhn.
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
}
