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
}
