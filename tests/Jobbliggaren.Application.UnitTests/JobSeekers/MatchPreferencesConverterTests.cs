using System.Reflection;
using System.Text.Json;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence.Configurations;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobSeekers;

// ADR 0079-amendment — the MatchPreferences jsonb converter must round-trip the new
// per-occupation experience overlay and stay forward-compatible (an old row written before
// the amendment lacks the key → empty overlay). The converter is Infrastructure-internal,
// exercised here through the EF ValueConverter (visible via InternalsVisibleTo, parity with
// HeadingDrivenResumeSegmenterTests). The real-Postgres jsonb path is covered end-to-end by
// MatchPreferencesJsonbBackcompatTests; this proves the pure serialization contract.
public class MatchPreferencesConverterTests
{
    private static string ToJson(MatchPreferences p) =>
        (string)MatchPreferencesConversion.Converter.ConvertToProvider(p)!;

    private static MatchPreferences FromJson(string json) =>
        (MatchPreferences)MatchPreferencesConversion.Converter.ConvertFromProvider(json)!;

    /// <summary>
    /// #551 — every stated dimension must be WRITTEN, not merely writable. The sibling guard
    /// (<c>MatchPreferencesContractParityTests</c>) binds the VO to the read projection and the
    /// write command, so a new dimension can no longer skip those. This one closes the fourth
    /// home, and it is the one with the worst failure mode: <c>Write</c> is a method body rather
    /// than a type surface, so a missing <c>WritePropertyName</c> line compiles. The value is then
    /// accepted, validated, saved with 204 — and read back as the type default, because
    /// <c>Read</c> is deliberately tolerant of a missing key (legacy rows). Silent data loss under
    /// a success status.
    ///
    /// <para>
    /// <c>Empty</c> is a sufficient probe, and a populated fixture would be WORSE: every dimension
    /// on <c>Create</c> has a default parameter, so a hand-written call keeps compiling when a
    /// dimension is added, and the probe would silently stop covering it. <c>Write</c> emits every
    /// key unconditionally in canonical form, so the empty VO exercises the full key set.
    /// </para>
    /// </summary>
    [Fact]
    public void Write_EmitsEveryStatedDimension_SoNoneCanBeSilentlyDropped()
    {
        using var doc = JsonDocument.Parse(ToJson(MatchPreferences.Empty));
        var written = doc.RootElement
            .EnumerateObject()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        var dimensions = typeof(MatchPreferences)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToArray();

        dimensions.ShouldNotBeEmpty(
            "the guard measures nothing if the VO exposes no public instance properties");

        var missing = dimensions
            .Where(name => !written.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        missing.ShouldBeEmpty(
            $"every stated MatchPreferences dimension must be written to the jsonb payload. "
            + $"Missing: {string.Join(", ", missing)}. A dimension Write omits is accepted, "
            + "validated and saved with 204, then read back as the type default — silent data "
            + "loss under a success status.");
    }

    [Fact]
    public void RoundTrip_PreservesOccupationExperienceOverlay()
    {
        var original = MatchPreferences.Create(
            preferredOccupationGroups: ["grp1", "grp2"],
            preferredRegions: null,
            preferredEmploymentTypes: null,
            preferredOccupationExperience:
            [
                new OccupationExperience("grp1", 5),
                new OccupationExperience("grp2", null),
            ]).Value;

        var restored = FromJson(ToJson(original));

        restored.ShouldBe(original);
        restored.PreferredOccupationExperience.Count.ShouldBe(2);
        restored.PreferredOccupationExperience.Single(e => e.ConceptId == "grp1").Years.ShouldBe(5);
        restored.PreferredOccupationExperience.Single(e => e.ConceptId == "grp2").Years.ShouldBeNull();
    }

    [Fact]
    public void Read_MissingOccupationExperienceKey_DefaultsToEmpty()
    {
        // An old job_seekers row written before the amendment has no PreferredOccupationExperience
        // key → empty overlay, never a crash (forward-compatible, parity with the other dimensions).
        const string oldRow =
            """{"PreferredOccupationGroups":["grp1"],"PreferredRegions":[],"PreferredEmploymentTypes":[],"PreferredMunicipalities":[],"PreferredSkills":[],"ExperienceYears":null}""";

        var restored = FromJson(oldRow);

        restored.PreferredOccupationGroups.ShouldBe(["grp1"]);
        restored.PreferredOccupationExperience.ShouldBeEmpty();
    }

    [Fact]
    public void Read_EmptyObject_DefaultsToEmptyOverlay()
    {
        var restored = FromJson("{}");

        restored.PreferredOccupationExperience.ShouldBeEmpty();
    }

    [Fact]
    public void Read_OrphanOverlayEntry_FailsClosedOnDomainInvariant()
    {
        // A stored overlay entry for a non-preferred group must fail the domain re-validation on
        // read (fail-safe on corruption), not silently load an incoherent VO.
        const string corrupt =
            """{"PreferredOccupationGroups":["grp1"],"PreferredOccupationExperience":[{"ConceptId":"grp2","Years":3}]}""";

        Should.Throw<JsonException>(() => FromJson(corrupt));
    }

    [Fact]
    public void Read_OverlayObjectMissingConceptId_FailsClosed()
    {
        const string corrupt =
            """{"PreferredOccupationGroups":["grp1"],"PreferredOccupationExperience":[{"Years":3}]}""";

        Should.Throw<JsonException>(() => FromJson(corrupt));
    }

    // #551 PR-B F3 — the remote/distans preference bool.
    [Fact]
    public void RoundTrip_PreservesPreferredRemote_True()
    {
        var original = MatchPreferences.Create(
            preferredOccupationGroups: ["grp1"],
            preferredRegions: null,
            preferredEmploymentTypes: null,
            preferredRemote: true).Value;

        var restored = FromJson(ToJson(original));

        restored.PreferredRemote.ShouldBeTrue();
        restored.ShouldBe(original);
    }

    [Fact]
    public void Read_MissingPreferredRemoteKey_DefaultsToFalse()
    {
        // A job_seekers row written before #551 has no PreferredRemote key → false (back-compat,
        // re-validated green in Create), never a crash.
        const string oldRow =
            """{"PreferredOccupationGroups":["grp1"],"PreferredRegions":[],"PreferredEmploymentTypes":[],"PreferredMunicipalities":[],"PreferredSkills":[],"ExperienceYears":null,"PreferredOccupationExperience":[]}""";

        var restored = FromJson(oldRow);

        restored.PreferredRemote.ShouldBeFalse();
    }

    [Fact]
    public void Read_NonBooleanPreferredRemote_FailsClosed()
    {
        // Default-deny (parity the other readers): a string/number/null in the bool key is rejected.
        const string corrupt = """{"PreferredRemote":"yes"}""";

        Should.Throw<JsonException>(() => FromJson(corrupt));
    }
}
