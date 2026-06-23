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
}
