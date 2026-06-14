using Jobbliggaren.Domain.Privacy;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Privacy;

// Fas 4 STEG 1 (F4-1) — Personnummer-guard, the highest-priority Fas-4
// invariant (ADR 0074 Invariant 1; CLAUDE.md §5; BUILD §13).
//
// RED PHASE: these tests are written BEFORE the detection/validation logic
// exists. The production stubs return false/empty, so every behavioural test
// here is expected to FAIL until GREEN. Naming: Method_Scenario_Expected.
//
// All vectors are SYNTHETIC test numbers constructed to pass/fail the Luhn
// check — not real identities (CLAUDE.md: no real personal data in tests).
public class PersonnummerTests
{
    // Canonical valid vectors (Luhn hand-verified) reused across PII-safety
    // assertions. Significant digits = the 10 that participate in Luhn.
    private const string ValidPnrSignificantDigits = "8112189876"; // 811218-9876
    private const string ValidSamordningSignificantDigits = "8112789873"; // 811278-9873

    // ===============================================================
    // Group A — TryParse happy paths: valid vectors return true + Kind
    // ===============================================================

    [Theory]
    [InlineData("811218-9876")] // 10-digit, hyphen separator
    [InlineData("19811218-9876")] // 12-digit, full century + hyphen
    [InlineData("811218+9876")] // plus separator (person is 100+)
    [InlineData("8112189876")] // 10 bare digits, no separator
    public void TryParse_ValidPersonnummer_ReturnsTrueWithPersonnummerKind(string candidate)
    {
        var ok = Personnummer.TryParse(candidate, out var result);

        ok.ShouldBeTrue();
        result.ShouldNotBeNull();
        result.Kind.ShouldBe(PersonnummerKind.Personnummer);
    }

    [Theory]
    [InlineData("811278-9873")] // day 18 + 60 = 78 → samordningsnummer
    [InlineData("8112789873")] // same, bare digits
    public void TryParse_ValidSamordningsnummer_ReturnsTrueWithSamordningsnummerKind(string candidate)
    {
        var ok = Personnummer.TryParse(candidate, out var result);

        ok.ShouldBeTrue();
        result.ShouldNotBeNull();
        result.Kind.ShouldBe(PersonnummerKind.Samordningsnummer);
    }

    // ===============================================================
    // Group B — TryParse rejections (one test per failure mode)
    // ===============================================================

    [Fact]
    public void TryParse_WrongLuhnCheckDigit_ReturnsFalse()
    {
        // 811218-9875: identical to the valid vector but last check digit is wrong.
        var ok = Personnummer.TryParse("811218-9875", out var result);

        ok.ShouldBeFalse();
        result.ShouldBeNull();
    }

    [Fact]
    public void TryParse_InvalidMonth_ReturnsFalseEvenWhenLuhnPasses()
    {
        // 991325-6765 — month field = 13 (impossible). Constructed so the Luhn
        // check over 9913256765 PASSES, proving date-sanity rejects INDEPENDENTLY
        // of Luhn. (Luhn sum of 9913256765 ≡ 0 mod 10.)
        var ok = Personnummer.TryParse("991325-6765", out var result);

        ok.ShouldBeFalse();
        result.ShouldBeNull();
    }

    [Fact]
    public void TryParse_InvalidDayTooHigh_ReturnsFalseEvenWhenLuhnPasses()
    {
        // 990541-2384 — day field = 41 (impossible for a real personnummer; not
        // a samordningsnummer either, which would need day 61–91). Constructed so
        // the Luhn check over 9905412384 PASSES, proving day-sanity rejects
        // INDEPENDENTLY of Luhn. (Luhn sum of 9905412384 ≡ 0 mod 10.)
        var ok = Personnummer.TryParse("990541-2384", out var result);

        ok.ShouldBeFalse();
        result.ShouldBeNull();
    }

    [Fact]
    public void TryParse_DayZero_ReturnsFalse()
    {
        // 811200-XXXX — day 00 is invalid regardless of Luhn.
        var ok = Personnummer.TryParse("811200-0009", out var result);

        ok.ShouldBeFalse();
        result.ShouldBeNull();
    }

    [Theory]
    [InlineData("81121-9876")] // too short (5 date digits)
    [InlineData("811218-987")] // too short (3 birth/check digits)
    [InlineData("811218-98765")] // too long (5 trailing digits)
    [InlineData("198112189876123")] // too long overall
    public void TryParse_WrongLength_ReturnsFalse(string candidate)
    {
        var ok = Personnummer.TryParse(candidate, out var result);

        ok.ShouldBeFalse();
        result.ShouldBeNull();
    }

    [Theory]
    [InlineData("8112AB9876")] // letters inside the digit field
    [InlineData("81121x-9876")] // stray non-digit
    [InlineData("")] // empty
    [InlineData("   ")] // whitespace only
    public void TryParse_NonDigitContent_ReturnsFalse(string candidate)
    {
        var ok = Personnummer.TryParse(candidate, out var result);

        ok.ShouldBeFalse();
        result.ShouldBeNull();
    }

    [Theory]
    [InlineData("0000000000")] // pure zeros — fails date and/or Luhn
    [InlineData("1234567890")] // generic reference number — fails Luhn/date
    public void TryParse_KnownNonPersonnummer_ReturnsFalse(string candidate)
    {
        var ok = Personnummer.TryParse(candidate, out var result);

        ok.ShouldBeFalse();
        result.ShouldBeNull();
    }

    // ===============================================================
    // Group C — Century / separator edge cases
    // ===============================================================

    [Fact]
    public void TryParse_HyphenAndPlusSeparator_BothParseToSameKind()
    {
        var hyphen = Personnummer.TryParse("811218-9876", out var hyphenResult);
        var plus = Personnummer.TryParse("811218+9876", out var plusResult);

        hyphen.ShouldBeTrue();
        plus.ShouldBeTrue();
        hyphenResult.ShouldNotBeNull();
        plusResult.ShouldNotBeNull();
        hyphenResult.Kind.ShouldBe(PersonnummerKind.Personnummer);
        plusResult.Kind.ShouldBe(PersonnummerKind.Personnummer);
    }

    [Fact]
    public void TryParse_TenAndTwelveDigitForms_BothParse()
    {
        var ten = Personnummer.TryParse("811218-9876", out _);
        var twelve = Personnummer.TryParse("19811218-9876", out _);

        ten.ShouldBeTrue();
        twelve.ShouldBeTrue();
    }

    [Fact]
    public void TryParse_YearFieldZeroZero_Parses()
    {
        // 001231-XXXX — the guard does not need the century to flag PII, so a
        // YY of "00" must still parse. 0012314720: Luhn over the digits ≡ 0.
        var ok = Personnummer.TryParse("001231-4720", out var result);

        ok.ShouldBeTrue();
        result.ShouldNotBeNull();
        result.Kind.ShouldBe(PersonnummerKind.Personnummer);
    }

    // ===============================================================
    // Group D — PII-safety invariants (HIGHEST PRIORITY)
    // ===============================================================

    [Fact]
    public void ToString_OnValidPersonnummer_ReturnsMaskedAndLeaksNoRawDigits()
    {
        Personnummer.TryParse("811218-9876", out var result).ShouldBeTrue();

        var rendered = result.ToString();

        rendered.ShouldBe(result.Masked);
        // The redacted form must not embed the run of real significant digits.
        rendered.ShouldNotContain(ValidPnrSignificantDigits);
        rendered.ShouldNotContain("8112189876");
    }

    [Fact]
    public void Masked_OnValidPersonnummer_ContainsNoRunOfRawDigits()
    {
        Personnummer.TryParse("811218-9876", out var result).ShouldBeTrue();

        result.Masked.ShouldNotBeNull();
        result.Masked.ShouldNotContain(ValidPnrSignificantDigits);
        // Also reject the separated form being echoed verbatim.
        result.Masked.ShouldNotContain("811218-9876");
    }

    [Fact]
    public void Masked_OnValidSamordningsnummer_ContainsNoRunOfRawDigits()
    {
        Personnummer.TryParse("811278-9873", out var result).ShouldBeTrue();

        result.Masked.ShouldNotBeNull();
        result.Masked.ShouldNotContain(ValidSamordningSignificantDigits);
        result.Masked.ShouldNotContain("811278-9873");
    }

    [Theory]
    [InlineData("811218-9876", "******-****")] // 10-digit, hyphen separator preserved
    [InlineData("811218+9876", "******+****")] // plus separator preserved
    [InlineData("8112189876", "**********")] // bare digits, no separator
    [InlineData("19811218-9876", "********-****")] // 12-digit, full century
    public void Masked_CanonicalForm_RedactsEveryDigitPreservingSeparatorAndLength(
        string candidate, string expectedMask)
    {
        Personnummer.TryParse(candidate, out var result).ShouldBeTrue();

        // Pin the canonical mask so a future change cannot silently start leaking
        // real digits: every digit becomes '*', the separator and overall length
        // are preserved.
        result.Masked.ShouldBe(expectedMask);
    }

    [Fact]
    public void PersonnummerMatch_ExposesNoRawValue_OnlyOffsetLengthKindMasked()
    {
        // The public surface of a match must be exactly StartOffset/Length/Kind/
        // Masked — no property that returns the raw value or a retained
        // Personnummer. Verified structurally so a future raw-value property
        // (a PII leak) breaks this test.
        var properties = typeof(PersonnummerMatch)
            .GetProperties()
            .Select(p => p.Name)
            .OrderBy(n => n)
            .ToArray();

        properties.ShouldBe(["Kind", "Length", "Masked", "StartOffset"]);

        typeof(PersonnummerMatch).GetProperty("Value").ShouldBeNull();
        typeof(PersonnummerMatch).GetProperty("Raw").ShouldBeNull();
        typeof(PersonnummerMatch).GetProperty("Personnummer").ShouldBeNull();
        typeof(PersonnummerMatch).GetProperty("Digits").ShouldBeNull();
    }

    // ===============================================================
    // Group F — Flag-only contract surface (design-intent, light)
    // ===============================================================

    [Fact]
    public void Personnummer_HasNoSuggestOrCreateFromNonTextSurface_FlagOnlyByDesign()
    {
        // ADR 0074 Invariant 1: the guard NEVER asks the user to add a
        // personnummer and never synthesises one. Structurally assert the type
        // exposes no factory beyond TryParse (no Create/Generate/Suggest/From*).
        var factoryNames = typeof(Personnummer)
            .GetMethods()
            .Where(m => m.IsStatic && m.IsPublic)
            .Select(m => m.Name)
            .ToArray();

        factoryNames.ShouldContain("TryParse");
        factoryNames.ShouldNotContain("Create");
        factoryNames.ShouldNotContain("Generate");
        factoryNames.ShouldNotContain("Suggest");
        factoryNames.ShouldNotContain(m => m.StartsWith("From", StringComparison.Ordinal));
    }
}
