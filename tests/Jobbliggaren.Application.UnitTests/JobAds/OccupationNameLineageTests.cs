using System.Text;
using System.Text.Json;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Infrastructure.Taxonomy;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds;

/// <summary>
/// #2199 — the occupation-name lineage rule on a hand-built graph.
/// </summary>
public sealed class OccupationNameLineageTests
{
    private static readonly string[] Current = ["30", "31"];
    private static readonly string[] Deprecated = [];

    // A ← B ← C: a single chain.                      N → {S, T}, S → T: a merge that is not a multi.
    // M → {X (G3), Y (G4)}: a multi split by group.   W → M: a predecessor chained into the multi.
    // Z → {P, Q}, both in G6: an ambiguous multi.     7 → nothing, no group: deprecated outright.
    private static OccupationNameLineage Lineage() => Load(
    [
        new("A", "G1", Current, []),
        new("B", "G1", Deprecated, ["A"]),
        new("C", "G1", Deprecated, ["B"]),
        new("N", "G2", Deprecated, ["S", "T"]),
        new("S", "G2", ["30"], ["T"]),
        new("T", "G2", ["31"], []),
        new("M", "G3", ["30"], ["X", "Y"]),
        new("W", "G3", Deprecated, ["M"]),
        new("X", "G3", Current, []),
        new("Y", "G4", Current, []),
        new("Z", "G6", Deprecated, ["P", "Q"]),
        new("P", "G6", Current, []),
        new("Q", "G6", Current, []),
        new("7", null, Deprecated, []),
    ]);

    private static OccupationNameLineage Load(IReadOnlyList<OccupationNameLineageEntry> entries)
    {
        var json = JsonSerializer.Serialize(new
        {
            lineageVersion = "test",
            taxonomyVersions = Current,
            occupationNames = entries.Select(e => new
            {
                conceptId = e.ConceptId,
                groupConceptId = e.GroupConceptId,
                currentIn = e.CurrentIn,
                replacedByConceptIds = e.ReplacedByConceptIds,
            }),
        });
        return OccupationNameLineageLoader.LoadFrom(new MemoryStream(Encoding.UTF8.GetBytes(json)));
    }

    private static readonly string[] Ids = ["A", "B", "C", "N", "S", "T", "M", "W", "X", "Y", "Z", "P", "Q", "7", "unknown"];
    private static readonly string?[] Groups = ["G1", "G2", "G3", "G4", "G5", "G6", null];

    [Theory]
    [InlineData("A", "G1", "A")]
    [InlineData("C", "G9", "A")]
    [InlineData("N", "G2", "T")]
    [InlineData("S", null, "T")]
    [InlineData("M", "G3", "X")]
    [InlineData("M", "G4", "Y")]
    [InlineData("W", "G4", "Y")]
    [InlineData("unknown", "G1", "unknown")]
    [InlineData("7", null, "7")]
    public void Resolve_FollowsTheNameToItsCurrentName(string name, string? group, string expected) =>
        Lineage().Resolve(name, group).ShouldBe(expected);

    [Theory]
    [InlineData("M", "G5")]
    [InlineData("M", null)]
    [InlineData("Z", "G6")]
    [InlineData("Z", null)]
    public void Resolve_AMultiWithoutOneTerminalInTheAdsGroup_ResolvesToNothing(string name, string? group) =>
        Lineage().Resolve(name, group).ShouldBeNull();

    [Fact]
    public void TargetOf_APickWithSeveralTerminals_HasNone() =>
        Lineage().TargetOf("M").ShouldBeNull();

    [Fact]
    public void Matches_AMultiPick_KeepsItsOwnAds()
    {
        var lineage = Lineage();

        lineage.Matches("M", "M", "G3").ShouldBeTrue();
        lineage.Matches("M", "M", "G5").ShouldBeTrue();
        lineage.Matches("M", "W", "G3").ShouldBeFalse();
        lineage.Matches("X", "M", "G3").ShouldBeTrue();
    }

    [Fact]
    public void Expand_ASingleChain_TakesEveryNameThatLeadsThere()
    {
        var expansion = Lineage().Expand(["A"]);

        expansion.ConceptIds.ShouldBe(["A", "B", "C"]);
        expansion.SameGroupOnly.ShouldBeEmpty();
    }

    [Fact]
    public void Expand_AMergeThatLeadsToOneName_IsNotAMulti() =>
        Lineage().Expand(["T"]).ConceptIds.ShouldBe(["N", "S", "T"]);

    [Fact]
    public void Expand_AnOlderPickOfTheSameName_TakesTheSameSet() =>
        Lineage().Expand(["S"]).ConceptIds.ShouldBe(["N", "S", "T"]);

    [Fact]
    public void Expand_ATerminalOfAMulti_TakesTheMultiOnlyInItsOwnGroup()
    {
        var expansion = Lineage().Expand(["X"]);

        expansion.ConceptIds.ShouldBe(["X"]);
        expansion.SameGroupOnly.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            s => s.GroupConceptId.ShouldBe("G3"),
            s => s.ConceptIds.ShouldBe(["M", "W"]));
    }

    [Fact]
    public void Expand_AMultiPick_TakesOnlyItself()
    {
        var expansion = Lineage().Expand(["M"]);

        expansion.ConceptIds.ShouldBe(["M"]);
        expansion.SameGroupOnly.ShouldBeEmpty();
    }

    [Fact]
    public void Expand_ATerminalOfAnAmbiguousMulti_TakesOnlyItself()
    {
        var expansion = Lineage().Expand(["P"]);

        expansion.ConceptIds.ShouldBe(["P"]);
        expansion.SameGroupOnly.ShouldBeEmpty();
    }

    [Fact]
    public void Expand_AnUnknownPick_TakesOnlyItself() =>
        Lineage().Expand(["unknown"]).ConceptIds.ShouldBe(["unknown"]);

    [Fact]
    public void Expand_NoPicks_IsEmpty() =>
        Lineage().Expand([]).IsEmpty.ShouldBeTrue();

    // The filter is exactly the rule: every (name, group) an ad can carry, for every single pick and
    // every pair of picks.
    [Fact]
    public void Expand_AdmitsExactlyThePairsMatchesAdmits()
    {
        var lineage = Lineage();
        var pickSets = Ids.Select(p => new[] { p })
            .Concat(Ids.SelectMany(a => Ids.Where(b => string.CompareOrdinal(a, b) < 0).Select(b => new[] { a, b })));

        foreach (var picks in pickSets)
        {
            var expansion = lineage.Expand(picks);
            foreach (var name in Ids)
            {
                foreach (var group in Groups)
                {
                    var byRule = picks.Any(p => lineage.Matches(p, name, group));
                    Admits(expansion, name, group).ShouldBe(byRule, $"picks [{string.Join(", ", picks)}], ad ({name}, {group ?? "null"})");
                }
            }
        }
    }

    [Fact]
    public void CountByPick_EqualsTheAdsMatchesAdmits_ForEveryCurrentName()
    {
        var lineage = Lineage();
        var rows = Ids.SelectMany((name, i) => Groups.Select((group, j) =>
            new OccupationNamePairCount(name, group, 1 + (i * 7) + j))).ToList();

        var counts = lineage.CountByPick(rows);

        foreach (var pick in lineage.CurrentConceptIds)
        {
            var expected = rows.Where(r => lineage.Matches(pick, r.OccupationNameConceptId, r.OccupationGroupConceptId)).Sum(r => r.Count);
            counts.GetValueOrDefault(pick).ShouldBe(expected, pick);
        }

        counts.Keys.ShouldBe(lineage.CurrentConceptIds, ignoreOrder: true);
    }

    [Fact]
    public void CountByPick_LeavesOutNamesWithNoAdsAndNamesThatAreNotCurrent()
    {
        var counts = Lineage().CountByPick([new OccupationNamePairCount("B", "G1", 3)]);

        counts.ShouldBe(new Dictionary<string, int> { ["A"] = 3 });
    }

    // #2199 PR 2b — the names a group files, read from the artefact's own groupConceptId: deprecated
    // names included, and never through a replaced-by edge (M → Y crosses into G4, which is not asked).
    [Fact]
    public void ConceptIdsInGroups_ReturnsEveryNameTheGroupsFile_DeprecatedIncluded() =>
        Lineage().ConceptIdsInGroups(["G1", "G3"]).ShouldBe(["A", "B", "C", "M", "W", "X"]);

    [Fact]
    public void ConceptIdsInGroups_NeverFollowsAReplacedByEdgeIntoTheGroup() =>
        Lineage().ConceptIdsInGroups(["G4"]).ShouldBe(["Y"]);

    [Fact]
    public void ConceptIdsInGroups_AGroupNoNameIsFiledUnder_IsEmpty() =>
        Lineage().ConceptIdsInGroups(["G5"]).ShouldBeEmpty();

    [Fact]
    public void ConceptIdsInGroups_NoGroups_IsEmpty() =>
        Lineage().ConceptIdsInGroups([]).ShouldBeEmpty();

    [Fact]
    public void Constructor_ACycle_Throws() =>
        Should.Throw<ArgumentException>(() => new OccupationNameLineage("test",
        [
            new("K1", "G1", Deprecated, ["K2"]),
            new("K2", "G1", Deprecated, ["K1"]),
        ])).Message.ShouldContain("cycle");

    [Fact]
    public void Constructor_AnEdgeToAnUnlistedName_Throws() =>
        Should.Throw<ArgumentException>(() => new OccupationNameLineage("test",
        [
            new("D", "G1", Deprecated, ["missing"]),
        ])).Message.ShouldContain("missing");

    [Fact]
    public void Constructor_ANameListedTwice_Throws() =>
        Should.Throw<ArgumentException>(() => new OccupationNameLineage("test",
        [
            new("A", "G1", Current, []),
            new("A", "G1", Current, []),
        ]));

    private static bool Admits(OccupationNameExpansion expansion, string name, string? group) =>
        expansion.ConceptIds.Contains(name)
        || expansion.SameGroupOnly.Any(s => s.GroupConceptId == group && s.ConceptIds.Contains(name));
}
