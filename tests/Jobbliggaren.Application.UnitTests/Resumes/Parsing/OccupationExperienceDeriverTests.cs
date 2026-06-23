using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Resumes.Parsing;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Parsing;

/// <summary>
/// ADR 0079-amendment (exp-per-occ PR-2) — the import-time per-occupation experience attribution
/// pass. The taxonomy deriver is mocked (its precision is OccupationCodeDeriver's own integration
/// suite); under test here is the attribution: re-derive per entry, parse its period, and
/// aggregate per group as the merged-interval UNION (Klas-val — never double-counting overlapping
/// roles), with the clock resolving "present" and the human-range cap applied.
/// </summary>
// CA2012: NSubstitute stubbing of ValueTask-returning port members is a known analyzer false
// positive (parity ImportResumeCommandHandlerTests).
#pragma warning disable CA2012
public class OccupationExperienceDeriverTests
{
    private const string GroupA = "q8wL_kdi_WaW";
    private const string GroupB = "a1B2_c3D4_e5F";

    // FakeDateTimeProvider.Default is 2026 → ongoing roles resolve "present" to 2026.
    private readonly IOccupationCodeDeriver _deriver = Substitute.For<IOccupationCodeDeriver>();
    private readonly OccupationExperienceDeriver _sut;

    public OccupationExperienceDeriverTests()
    {
        // Default: any source derives nothing, so an unstubbed entry never NREs on a default
        // ValueTask. Specific stubs (configured later, last-match-wins) override per title.
        _deriver.DeriveManyAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Derivation());
        _sut = new OccupationExperienceDeriver(_deriver, FakeDateTimeProvider.Default);
    }

    private static ValueTask<OccupationDerivationResult> Derivation(params string[] groupConceptIds) =>
        new(new OccupationDerivationResult(
            "echo",
            groupConceptIds
                .Select(id => new OccupationCandidate(
                    id, $"label-{id}", OccupationMatchKind.ExactOccupationName, "matched"))
                .ToList()));

    // Stub: an experience whose sources contain <title> derives <groupConceptIds>.
    private void DeriveTitleTo(string title, params string[] groupConceptIds) =>
        _deriver.DeriveManyAsync(
                Arg.Is<IReadOnlyList<string>>(s => s.Contains(title)), Arg.Any<CancellationToken>())
            .Returns(Derivation(groupConceptIds));

    private async Task<IReadOnlyDictionary<string, int>> Derive(params ParsedExperience[] experiences) =>
        await _sut.DeriveApproximateYearsAsync(experiences, CancellationToken.None);

    [Fact]
    public async Task SingleEntry_AttributesEndMinusStartYears_ToItsGroup()
    {
        DeriveTitleTo("Systemutvecklare", GroupA);

        var years = await Derive(new ParsedExperience("Systemutvecklare", "Acme AB", "2019–2024", "raw"));

        years.ShouldContainKeyAndValue(GroupA, 5);
    }

    [Fact]
    public async Task OngoingRole_UsesClockYearForPresentEnd()
    {
        DeriveTitleTo("Operatör", GroupA);

        var years = await Derive(new ParsedExperience("Operatör", "Plast AB", "2005–nu", "raw"));

        // 2026 (FakeDateTimeProvider.Default) − 2005 = 21 — never DateTime.Now.
        years.ShouldContainKeyAndValue(GroupA, 21);
    }

    [Fact]
    public async Task OverlappingSpansSameGroup_AreMergedNotSummed()
    {
        // Two concurrent roles in the same group must not double-count: [2018,2024] ∪ [2020,2022]
        // = [2018,2024] = 6 years (NOT 6 + 2 = 8).
        DeriveTitleTo("Roll1", GroupA);
        DeriveTitleTo("Roll2", GroupA);

        var years = await Derive(
            new ParsedExperience("Roll1", "Org1", "2018–2024", "raw"),
            new ParsedExperience("Roll2", "Org2", "2020–2022", "raw"));

        years.ShouldContainKeyAndValue(GroupA, 6);
    }

    [Fact]
    public async Task DisjointSpansSameGroup_AreSummed()
    {
        // Two non-overlapping stints in the same field: [2010,2012] + [2018,2020] = 2 + 2 = 4.
        DeriveTitleTo("Roll1", GroupA);
        DeriveTitleTo("Roll2", GroupA);

        var years = await Derive(
            new ParsedExperience("Roll1", "Org1", "2010–2012", "raw"),
            new ParsedExperience("Roll2", "Org2", "2018–2020", "raw"));

        years.ShouldContainKeyAndValue(GroupA, 4);
    }

    [Fact]
    public async Task AdjacentSpansSameGroup_MergeWithoutGapOrDoubleCount()
    {
        // Back-to-back roles sharing a boundary year: [2018,2020] ∪ [2020,2024] = [2018,2024] = 6.
        DeriveTitleTo("Roll1", GroupA);
        DeriveTitleTo("Roll2", GroupA);

        var years = await Derive(
            new ParsedExperience("Roll1", "Org1", "2018–2020", "raw"),
            new ParsedExperience("Roll2", "Org2", "2020–2024", "raw"));

        years.ShouldContainKeyAndValue(GroupA, 6);
    }

    [Fact]
    public async Task SinglePointYear_AttributesZero_GroupPresentWithZero()
    {
        // A bare year-only role ("2020") is a zero-length span → 0 years. The group is PRESENT
        // with value 0 (a parsed, cited fact: "less than a year"), distinct from an ABSENT group
        // (null = "not stated"). 0-vs-null is the load-bearing honesty distinction (§5).
        DeriveTitleTo("Roll1", GroupA);

        var years = await Derive(new ParsedExperience("Roll1", "Org1", "2020", "raw"));

        years.ShouldContainKeyAndValue(GroupA, 0); // present-with-0, NOT absent
    }

    [Fact]
    public async Task ThreeSpanChain_MergesOverlappingRunThenAddsDisjoint()
    {
        // Exercises the mid-loop run-close (not just the final flush): [2010,2012] ∪ [2011,2013]
        // = [2010,2013] (3), then disjoint [2018,2020] (2) → 5.
        DeriveTitleTo("Roll1", GroupA);
        DeriveTitleTo("Roll2", GroupA);
        DeriveTitleTo("Roll3", GroupA);

        var years = await Derive(
            new ParsedExperience("Roll1", "Org1", "2010–2012", "raw"),
            new ParsedExperience("Roll2", "Org2", "2011–2013", "raw"),
            new ParsedExperience("Roll3", "Org3", "2018–2020", "raw"));

        years.ShouldContainKeyAndValue(GroupA, 5);
    }

    [Fact]
    public async Task OngoingSpanMergedWithClosedSpan_SameGroup()
    {
        // A current role overlapping a past stint in the same field: [2015,2020] ∪
        // [2018,present(2026)] = [2015,2026] = 11 (clock year, no double-count).
        DeriveTitleTo("Roll1", GroupA);
        DeriveTitleTo("Roll2", GroupA);

        var years = await Derive(
            new ParsedExperience("Roll1", "Org1", "2015–2020", "raw"),
            new ParsedExperience("Roll2", "Org2", "2018–nu", "raw"));

        years.ShouldContainKeyAndValue(GroupA, 11);
    }

    [Fact]
    public async Task DistinctGroups_EachAggregatedIndependently()
    {
        DeriveTitleTo("Systemutvecklare", GroupA);
        DeriveTitleTo("Sjuksköterska", GroupB);

        var years = await Derive(
            new ParsedExperience("Systemutvecklare", "Acme", "2019–2024", "raw"),
            new ParsedExperience("Sjuksköterska", "Vården", "2010–2013", "raw"));

        years.ShouldContainKeyAndValue(GroupA, 5);
        years.ShouldContainKeyAndValue(GroupB, 3);
    }

    [Fact]
    public async Task EntryDerivingTwoGroups_AttributesItsSpanToBoth()
    {
        DeriveTitleTo("Systemutvecklare", GroupA, GroupB);

        var years = await Derive(new ParsedExperience("Systemutvecklare", "Acme", "2019–2024", "raw"));

        years.ShouldContainKeyAndValue(GroupA, 5);
        years.ShouldContainKeyAndValue(GroupB, 5);
    }

    [Fact]
    public async Task UnparseablePeriod_ContributesNothing_GroupAbsent()
    {
        DeriveTitleTo("Systemutvecklare", GroupA);

        var years = await Derive(
            new ParsedExperience("Systemutvecklare", "Acme", "ett tag sedan", "raw"));

        years.ShouldNotContainKey(GroupA); // honest "not stated" → null at the call-site
    }

    [Fact]
    public async Task MissingPeriod_ContributesNothing_GroupAbsent()
    {
        DeriveTitleTo("Systemutvecklare", GroupA);

        var years = await Derive(new ParsedExperience("Systemutvecklare", "Acme", null, "raw"));

        years.ShouldNotContainKey(GroupA);
    }

    [Fact]
    public async Task GroupWithMixedParseableAndUnparseable_CountsOnlyTheParseableSpan()
    {
        DeriveTitleTo("Roll1", GroupA);
        DeriveTitleTo("Roll2", GroupA);

        var years = await Derive(
            new ParsedExperience("Roll1", "Org1", "2019–2024", "raw"), // 5
            new ParsedExperience("Roll2", "Org2", "fritext", "raw"));  // skipped

        years.ShouldContainKeyAndValue(GroupA, 5);
    }

    [Fact]
    public async Task DerivedSpanBeyondHumanRange_IsCappedAtMaxExperienceYears()
    {
        // Defensive SPOT cap: a CV-derived seed can never exceed the write-path invariant bound,
        // so the FE number input (max 70) and the derived value cannot disagree.
        DeriveTitleTo("Veteran", GroupA);

        var years = await Derive(new ParsedExperience("Veteran", "Org", "1900–nu", "raw")); // 126

        years.ShouldContainKeyAndValue(GroupA, MatchPreferences.MaxExperienceYears);
    }

    [Fact]
    public async Task NoExperiences_ReturnsEmpty()
    {
        var years = await Derive();

        years.ShouldBeEmpty();
    }

    [Fact]
    public async Task EntryThatDerivesNoGroup_ContributesNothing()
    {
        // A company/free-text entry the deriver self-filters to no match: even with a valid
        // period, nothing is attributed (no group to attribute it to).
        var years = await Derive(new ParsedExperience("Acme AB", null, "2019–2024", "raw"));

        years.ShouldBeEmpty();
    }
}
