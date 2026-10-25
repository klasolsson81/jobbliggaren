using System.Text.Json;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Infrastructure.Taxonomy;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Taxonomy;

/// <summary>
/// #2199 — the committed occupation-name lineage, through the real loader. The premises below are the
/// shapes measured against JobTech's taxonomy on 2026-10-24 that the /jobb occupation-name filter rests
/// on.
/// </summary>
public sealed class OccupationNameLineageArtefactTests
{
    private const string ResourceName = "Jobbliggaren.Infrastructure.Taxonomy.occupation-name-lineage.json";
    private const string SnapshotResourceName = "Jobbliggaren.Infrastructure.Taxonomy.taxonomy-snapshot.json";

    private static readonly Lazy<OccupationNameLineage> RealLineage = new(OccupationNameLineageLoader.Load);

    [Fact]
    public void TheCommittedArtefact_Loads()
    {
        var lineage = RealLineage.Value;

        lineage.Version.ShouldBe("1");
        lineage.CurrentConceptIds.Count.ShouldBeGreaterThan(2_000);
    }

    [Fact]
    public void TheCommittedArtefact_IsSortedByConceptIdAndUnique()
    {
        var ids = ReadEntries().Select(e => e.GetProperty("conceptId").GetString()!).ToList();

        ids.ShouldBe(ids.Order(StringComparer.Ordinal).ToList(), "the generator sorts by conceptId, Ordinal");
        ids.Distinct(StringComparer.Ordinal).Count().ShouldBe(ids.Count);
    }

    [Fact]
    public void TheCommittedArtefact_NamesItsVersions()
    {
        using var document = JsonDocument.Parse(Resource(ResourceName));
        var root = document.RootElement;

        root.GetProperty("taxonomyVersions").EnumerateArray().Select(v => v.GetString()).ShouldBe(["30", "31"]);
        root.GetProperty("fetchedAt").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    // The /jobb picker offers the v30 names that the frozen crosswalk maps; each must be a name the
    // lineage knows as current in v30, or its pick would match only its own id.
    [Fact]
    public void EveryPickerName_IsCurrentInV30()
    {
        var currentIn = ReadEntries().ToDictionary(
            e => e.GetProperty("conceptId").GetString()!,
            e => e.GetProperty("currentIn").EnumerateArray().Select(v => v.GetString()!).ToList(),
            StringComparer.Ordinal);
        var crosswalk = OccupationGroupMappingLoader.Load();
        using var snapshot = JsonDocument.Parse(Resource(SnapshotResourceName));
        var pickerNames = snapshot.RootElement.GetProperty("occupationFields").EnumerateArray()
            .SelectMany(f => f.GetProperty("occupations").EnumerateArray())
            .Select(o => o.GetProperty("conceptId").GetString()!)
            .Where(crosswalk.ContainsKey)
            .ToList();

        pickerNames.Count.ShouldBe(2_153, "premise: the picker's mapped names, measured 2026-10-24");
        pickerNames.Where(id => !currentIn.TryGetValue(id, out var versions) || !versions.Contains("30")).ShouldBeEmpty();
    }

    // Projektledare, IT: current, no predecessors, in Mjukvaru- och systemutvecklare m.fl. Its pick is
    // its own id and nothing else (JobTech: 136 ads, all its own, 2026-10-24).
    [Fact]
    public void ProjektledareIt_IsItsOwnName()
    {
        var lineage = RealLineage.Value;

        lineage.Resolve("z5AM_ayf_WcL", "DJh5_yyF_hEM").ShouldBe("z5AM_ayf_WcL");
        lineage.Expand(["z5AM_ayf_WcL"]).ShouldSatisfyAllConditions(
            e => e.ConceptIds.ShouldBe(["z5AM_ayf_WcL"]),
            e => e.SameGroupOnly.ShouldBeEmpty());
    }

    // Grundlärare, 4-6 (S9LC) was replaced in v31 by Grundlärare, 4–6 (743P), which also replaced
    // Lärare i grundskolan, årskurs 4-6 (Nzmh). Nzmh's raw edges name both S9LC (v30) and 743P (v31):
    // a merge, not a multi. JobTech returns the same 208 ads for all three ids (2026-10-24).
    [Fact]
    public void Grundlarare46_IsOneNameAcrossBothVersions()
    {
        var lineage = RealLineage.Value;

        lineage.Resolve("Nzmh_iTb_z7h", "oQUQ_D11_HPx").ShouldBe("743P_CSD_tF8");
        lineage.TargetOf("S9LC_4m4_yza").ShouldBe("743P_CSD_tF8");
        lineage.Expand(["S9LC_4m4_yza"]).ConceptIds.ShouldBe(["743P_CSD_tF8", "Nzmh_iTb_z7h", "S9LC_4m4_yza"]);
        lineage.Expand(["743P_CSD_tF8"]).ConceptIds.ShouldBe(["743P_CSD_tF8", "Nzmh_iTb_z7h", "S9LC_4m4_yza"]);
    }

    // The generic Specialistläkare was replaced by its 18 specialisms, all in its own group, so no
    // specialism can claim its ads: they stay reachable through the group only.
    [Fact]
    public void Specialistlakare_ResolvesToNoSpecialism()
    {
        var lineage = RealLineage.Value;

        lineage.Resolve("oHyy_M2j_HUP", "nu7c_QuB_KVQ").ShouldBeNull();
        lineage.Resolve("oHyy_M2j_HUP", null).ShouldBeNull();
    }

    [Fact]
    public void LarareIFritidshem_ResolvesToNeitherSuccessor() =>
        RealLineage.Value.Resolve("wHUj_vsP_Jhn", "CFW8_eBa_NaV").ShouldBeNull();

    // A multi-replacement name whose successors sit in different groups attaches by the ad's group.
    [Fact]
    public void AMultiSplitByGroup_ResolvesByTheAdsGroup()
    {
        var lineage = RealLineage.Value;
        var entries = ReadEntries().ToDictionary(e => e.GetProperty("conceptId").GetString()!, StringComparer.Ordinal);
        string? Group(string id) => entries[id].GetProperty("groupConceptId").GetString();

        var split = entries.Values
            .Where(e => e.GetProperty("replacedByConceptIds").GetArrayLength() > 1)
            .Select(e => e.GetProperty("conceptId").GetString()!)
            .Select(id => (Id: id, Targets: entries[id].GetProperty("replacedByConceptIds").EnumerateArray()
                .Select(t => t.GetString()!).ToList()))
            .FirstOrDefault(m => m.Targets.Select(Group).Distinct().Count() == m.Targets.Count
                && m.Targets.All(t => lineage.TargetOf(t) == t));
        split.Id.ShouldNotBeNull("premise: the artefact holds a multi whose current successors are in distinct groups");

        foreach (var target in split.Targets)
            lineage.Resolve(split.Id, Group(target)).ShouldBe(target);
    }

    // The filter is exactly the rule on the real graph: for every pick, the (name, group) pairs the
    // expansion admits are the ones Matches admits. Groups are taken per name from its terminals,
    // plus one group none of them has and none at all, which is every case the rule distinguishes.
    [Fact]
    public void Expand_AdmitsExactlyThePairsMatchesAdmits_ForEveryName()
    {
        var lineage = RealLineage.Value;
        var entries = ReadEntries().ToList();
        var ids = entries.Select(e => e.GetProperty("conceptId").GetString()!).ToList();
        var groupOf = entries.ToDictionary(
            e => e.GetProperty("conceptId").GetString()!,
            e => e.GetProperty("groupConceptId").GetString(),
            StringComparer.Ordinal);
        const string otherGroup = "no_such_group";

        // Every pick whose single terminal is t, so a pair's matching picks come from its resolution.
        var picksByTarget = ids
            .Where(id => lineage.TargetOf(id) is not null)
            .GroupBy(id => lineage.TargetOf(id)!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        // The test's own walk of the raw edges, independent of the lineage's.
        var edges = entries.ToDictionary(
            e => e.GetProperty("conceptId").GetString()!,
            e => e.GetProperty("replacedByConceptIds").EnumerateArray().Select(t => t.GetString()!).ToList(),
            StringComparer.Ordinal);
        IEnumerable<string> Terminals(string id) =>
            edges[id].Count == 0 ? [id] : edges[id].SelectMany(Terminals);

        var expected = ids.ToDictionary(id => id, _ => new HashSet<(string, string?)>(), StringComparer.Ordinal);
        foreach (var name in ids)
        {
            var groups = new HashSet<string?>(StringComparer.Ordinal) { otherGroup, null, groupOf[name] };
            foreach (var terminal in Terminals(name))
                groups.Add(groupOf[terminal]);
            foreach (var group in groups)
            {
                expected[name].Add((name, group));
                if (lineage.Resolve(name, group) is { } resolved && picksByTarget.TryGetValue(resolved, out var picks))
                {
                    foreach (var pick in picks)
                        expected[pick].Add((name, group));
                }
            }
        }

        foreach (var pick in ids)
        {
            var expansion = lineage.Expand([pick]);
            var admitted = expected[pick].Where(pair => Admits(expansion, pair.Item1, pair.Item2)).ToHashSet();
            admitted.SetEquals(expected[pick]).ShouldBeTrue($"{pick}: the expansion misses a pair Matches admits");
            foreach (var pair in expected[pick])
                lineage.Matches(pick, pair.Item1, pair.Item2).ShouldBeTrue($"{pick} vs ({pair.Item1}, {pair.Item2})");
            foreach (var name in expansion.ConceptIds)
                lineage.Matches(pick, name, otherGroup).ShouldBeTrue($"{pick} admits {name} in any group");
            foreach (var sameGroup in expansion.SameGroupOnly)
            {
                foreach (var name in sameGroup.ConceptIds)
                {
                    lineage.Matches(pick, name, sameGroup.GroupConceptId).ShouldBeTrue($"{pick} admits {name} in {sameGroup.GroupConceptId}");
                    lineage.Matches(pick, name, otherGroup).ShouldBeFalse($"{pick} admits {name} only in {sameGroup.GroupConceptId}");
                }
            }
        }
    }

    private static bool Admits(OccupationNameExpansion expansion, string name, string? group) =>
        expansion.ConceptIds.Contains(name)
        || expansion.SameGroupOnly.Any(s => s.GroupConceptId == group && s.ConceptIds.Contains(name));

    private static List<JsonElement> ReadEntries()
    {
        using var document = JsonDocument.Parse(Resource(ResourceName));
        return [.. document.RootElement.GetProperty("occupationNames").EnumerateArray().Select(e => e.Clone())];
    }

    private static Stream Resource(string name) =>
        typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly.GetManifestResourceStream(name)
        ?? throw new InvalidOperationException($"Embedded resource {name} is missing.");
}
