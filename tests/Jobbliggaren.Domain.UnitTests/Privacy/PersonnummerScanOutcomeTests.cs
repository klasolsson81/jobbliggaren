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

    // The camelCase options ParsedResumeConfiguration.JsonOptions persists this outcome with
    // (#426 back-compat tests). Cached once (CA1869 — never per-call).
    private static readonly System.Text.Json.JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
    };

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
    public void PersonnummerScanOutcome_ExposesNoRawValueOrOffset_OnlyFoundCountKindsAndLocationFlag()
    {
        // PII-safety boundary: the type must carry ONLY Found/Count/Kinds plus the non-PII
        // FoundInFileName LOCATION flag (#426) — no raw value, no offset, no masked-value
        // member, no raw filename. Verified structurally so a future PII-leaking member
        // breaks this test.
        var properties = typeof(PersonnummerScanOutcome)
            .GetProperties(System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public)
            .Where(p => p.Name != "EqualityContract") // record-synthesized
            .Select(p => p.Name)
            .OrderBy(n => n)
            .ToArray();

        properties.ShouldBe(["Count", "Found", "FoundInFileName", "Kinds"]);

        // FoundInFileName is a bare bool — a location discriminator, not a PII channel.
        typeof(PersonnummerScanOutcome).GetProperty("FoundInFileName")!
            .PropertyType.ShouldBe(typeof(bool));

        typeof(PersonnummerScanOutcome).GetProperty("Value").ShouldBeNull();
        typeof(PersonnummerScanOutcome).GetProperty("Raw").ShouldBeNull();
        typeof(PersonnummerScanOutcome).GetProperty("Offsets").ShouldBeNull();
        typeof(PersonnummerScanOutcome).GetProperty("Masked").ShouldBeNull();
        typeof(PersonnummerScanOutcome).GetProperty("Matches").ShouldBeNull();
        typeof(PersonnummerScanOutcome).GetProperty("FileName").ShouldBeNull();
    }

    // ===============================================================
    // #426 — filename personnummer flag (Variant B, senior-cto-advisor). The filename
    // scan rides as a SEPARATE bool: Found/Count/Kinds stay BODY-exclusive, so a
    // filename-only hit never blocks promotion; B4 surfaces it as a Warn instead.
    // ===============================================================

    [Fact]
    public void None_FoundInFileName_IsFalse()
    {
        PersonnummerScanOutcome.None.FoundInFileName.ShouldBeFalse();
    }

    [Fact]
    public void FromMatches_BodyMatches_DefaultsFoundInFileNameFalse()
    {
        // The single-arg call path (PromoteParsedResume and the pre-#426 body scan) leaves
        // FoundInFileName at its safe default.
        var outcome = PersonnummerScanOutcome.FromMatches(PersonnummerScanner.Scan($"Pnr {Pnr}."));

        outcome.Found.ShouldBeTrue();
        outcome.FoundInFileName.ShouldBeFalse();
    }

    [Fact]
    public void FromMatches_EmptyBody_ButFoundInFileName_FoundStaysFalse_ButFlagSet()
    {
        // A clean CV body but a personnummer in the FILENAME: Found (the body signal that
        // gates promotion) stays FALSE, Count 0, Kinds empty — only FoundInFileName is set.
        // This is NOT the None singleton (a filename hit must survive round-trip).
        var outcome = PersonnummerScanOutcome.FromMatches([], foundInFileName: true);

        outcome.Found.ShouldBeFalse();
        outcome.Count.ShouldBe(0);
        outcome.Kinds.ShouldBeEmpty();
        outcome.FoundInFileName.ShouldBeTrue();
        outcome.ShouldNotBeSameAs(PersonnummerScanOutcome.None);
    }

    [Fact]
    public void FromMatches_BodyAndFileName_BodyFieldsBodyOnly_FlagSet()
    {
        // Both surfaces carry a personnummer: Found/Count/Kinds describe the BODY ONLY
        // (Count is the body count, not a body+filename union), while FoundInFileName is true.
        var body = PersonnummerScanner.Scan($"A {Pnr} och B {Pnr}."); // 2 body detections
        var outcome = PersonnummerScanOutcome.FromMatches(body, foundInFileName: true);

        outcome.Found.ShouldBeTrue();
        outcome.Count.ShouldBe(2); // body-exclusive — the filename is NOT counted in
        outcome.Kinds.ShouldBe([PersonnummerKind.Personnummer]);
        outcome.FoundInFileName.ShouldBeTrue();
    }

    [Fact]
    public void FromMatches_EmptyBody_NoFileName_ReturnsNone()
    {
        PersonnummerScanOutcome.FromMatches([], foundInFileName: false)
            .ShouldBeSameAs(PersonnummerScanOutcome.None);
    }

    // ===============================================================
    // #426 — jsonb back-compat: an outcome persisted BEFORE this slice has no
    // `foundInFileName` key. It must round-trip to the safe default false (never a false
    // alarm), matching ParsedResumeConfiguration.JsonOptions (camelCase).
    // ===============================================================

    [Fact]
    public void Deserialize_LegacyJsonWithoutFoundInFileName_DefaultsToFalse()
    {
        // Exactly the shape ParsedResumeConfiguration.JsonOptions (camelCase) wrote before #426.
        const string legacyJson = """{"found":true,"count":1,"kinds":[0]}""";

        var outcome = System.Text.Json.JsonSerializer
            .Deserialize<PersonnummerScanOutcome>(legacyJson, CamelCase);

        outcome.ShouldNotBeNull();
        outcome.Found.ShouldBeTrue();
        outcome.Count.ShouldBe(1);
        outcome.Kinds.ShouldBe([PersonnummerKind.Personnummer]);
        outcome.FoundInFileName.ShouldBeFalse(); // missing key → safe default, no false alarm
    }

    [Fact]
    public void Serialize_ThenDeserialize_RoundTripsFoundInFileName()
    {
        var outcome = PersonnummerScanOutcome.FromMatches([], foundInFileName: true);

        var json = System.Text.Json.JsonSerializer.Serialize(outcome, CamelCase);
        var roundTripped = System.Text.Json.JsonSerializer
            .Deserialize<PersonnummerScanOutcome>(json, CamelCase);

        json.ShouldContain("foundInFileName");
        roundTripped!.FoundInFileName.ShouldBeTrue();
        roundTripped.Found.ShouldBeFalse();
    }
}
