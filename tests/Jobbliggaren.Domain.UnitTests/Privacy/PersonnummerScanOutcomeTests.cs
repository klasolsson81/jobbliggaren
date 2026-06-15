using Jobbliggaren.Domain.Privacy;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Privacy;

// Fas 4 STEG 8 (F4-8, ADR 0074 Invariant 1) — PersonnummerScanOutcome projects
// scanner matches into the PII-SAFE summary the F4-8 call-site carries on the
// aggregate: count + distinct kinds only, NEVER a raw value or an offset
// (surfacing offsets into persisted PII would be a reconstruction aid). The match
// list is obtained via the public PersonnummerScanner.Scan surface (the factory
// PersonnummerMatch.Create is internal by design).
//
// SPEC-DRIVEN. All vectors are SYNTHETIC Luhn-valid test numbers.
public class PersonnummerScanOutcomeTests
{
    private const string Pnr = "811218-9876"; // personnummer
    private const string Samordning = "811278-9873"; // samordningsnummer

    [Fact]
    public void None_IsTheCleanOutcome_FoundFalseCountZeroKindsEmpty()
    {
        PersonnummerScanOutcome.None.Found.ShouldBeFalse();
        PersonnummerScanOutcome.None.Count.ShouldBe(0);
        PersonnummerScanOutcome.None.Kinds.ShouldBeEmpty();
    }

    [Fact]
    public void FromMatches_EmptyList_ReturnsNone()
    {
        var outcome = PersonnummerScanOutcome.FromMatches([]);

        outcome.Found.ShouldBeFalse();
        outcome.Count.ShouldBe(0);
        outcome.Kinds.ShouldBeEmpty();
        outcome.ShouldBeSameAs(PersonnummerScanOutcome.None);
    }

    [Fact]
    public void FromMatches_NullList_ReturnsNone()
    {
        var outcome = PersonnummerScanOutcome.FromMatches(null!);

        outcome.ShouldBeSameAs(PersonnummerScanOutcome.None);
    }

    [Fact]
    public void FromMatches_SinglePersonnummer_FoundTrueCountOneKindPersonnummer()
    {
        var matches = PersonnummerScanner.Scan($"Pnr {Pnr} i CV.");

        var outcome = PersonnummerScanOutcome.FromMatches(matches);

        outcome.Found.ShouldBeTrue();
        outcome.Count.ShouldBe(1);
        outcome.Kinds.ShouldBe([PersonnummerKind.Personnummer]);
    }

    [Fact]
    public void FromMatches_RepeatedSameNumber_CountsEachDetection_KindsDistinct()
    {
        // The same number twice ⇒ Count = 2 (not de-duplicated — the user must
        // remove all of them) but Kinds is the DISTINCT set.
        var matches = PersonnummerScanner.Scan($"A {Pnr} och B {Pnr}.");

        var outcome = PersonnummerScanOutcome.FromMatches(matches);

        outcome.Found.ShouldBeTrue();
        outcome.Count.ShouldBe(2);
        outcome.Kinds.ShouldBe([PersonnummerKind.Personnummer]);
    }

    [Fact]
    public void FromMatches_MixedKinds_KindsAreDistinctAndOrderedByEnumValue()
    {
        // Personnummer (enum 0) before Samordningsnummer (enum 1), distinct.
        var matches = PersonnummerScanner.Scan(
            $"Kandidat {Pnr} och {Samordning} samt ytterligare {Pnr}.");

        var outcome = PersonnummerScanOutcome.FromMatches(matches);

        outcome.Found.ShouldBeTrue();
        outcome.Count.ShouldBe(3); // three detections
        outcome.Kinds.ShouldBe(
            [PersonnummerKind.Personnummer, PersonnummerKind.Samordningsnummer]);
    }

    [Fact]
    public void PersonnummerScanOutcome_ExposesNoRawValueOrOffset_OnlyFoundCountKinds()
    {
        // PII-safety boundary: the type must carry ONLY Found/Count/Kinds — no raw
        // value, no offset, no masked-value member. Verified structurally so a
        // future PII-leaking member breaks this test.
        var properties = typeof(PersonnummerScanOutcome)
            .GetProperties(System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public)
            .Where(p => p.Name != "EqualityContract") // record-synthesized
            .Select(p => p.Name)
            .OrderBy(n => n)
            .ToArray();

        properties.ShouldBe(["Count", "Found", "Kinds"]);

        typeof(PersonnummerScanOutcome).GetProperty("Value").ShouldBeNull();
        typeof(PersonnummerScanOutcome).GetProperty("Raw").ShouldBeNull();
        typeof(PersonnummerScanOutcome).GetProperty("Offsets").ShouldBeNull();
        typeof(PersonnummerScanOutcome).GetProperty("Masked").ShouldBeNull();
        typeof(PersonnummerScanOutcome).GetProperty("Matches").ShouldBeNull();
    }
}
