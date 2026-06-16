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
/// <see cref="PersonnummerScanner.Scan"/> and replaces each matched span (right-to-left by
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

    // The mask the real Personnummer.BuildMask produces for either delimited 10-digit token:
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
}
