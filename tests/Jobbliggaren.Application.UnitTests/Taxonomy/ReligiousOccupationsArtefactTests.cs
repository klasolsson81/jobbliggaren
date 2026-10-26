using System.Text.Json;
using Jobbliggaren.Application.RecentJobSearches.Common;
using Jobbliggaren.Infrastructure.Taxonomy;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Taxonomy;

/// <summary>
/// #2199 PR 2b — the religious occupations through the real loaders, against the committed artefacts read
/// independently as raw JSON. Nothing in the guard is typed except the four group ids, so this is where a
/// mistake in the derivation, the field coverage or the constant fails.
/// </summary>
public sealed class ReligiousOccupationsArtefactTests
{
    private const string LineageResourceName = "Jobbliggaren.Infrastructure.Taxonomy.occupation-name-lineage.json";
    private const string SnapshotResourceName = "Jobbliggaren.Infrastructure.Taxonomy.taxonomy-snapshot.json";

    private static readonly Lazy<ReligiousOccupations> Real =
        new(() => new ReligiousOccupations(OccupationNameLineageLoader.Load(), TaxonomySnapshotLoader.Load()));

    // Both directions over every name the artefact holds: a name derived in by mistake (through a replaced-by
    // edge, say) fails as surely as one left out. On the name axis a policy group id is refused too.
    [Fact]
    public void OnTheNameAxis_ExactlyTheNamesFiledUnderTheFourGroups_AreSingledOut()
    {
        var expected = NamesFiledUnderTheFourGroups().Concat(ReligiousOccupationGroups.ConceptIds)
            .ToHashSet(StringComparer.Ordinal);
        var probed = ReadLineageEntries().Select(e => e.GetProperty("conceptId").GetString()!)
            .Concat(SnapshotFields().SelectMany(f => f.Groups))
            .Distinct(StringComparer.Ordinal);

        probed.Where(id => Real.Value.AreSingledOutBy([], [id]) != expected.Contains(id)).ShouldBeEmpty();
    }

    [Fact]
    public void OnTheGroupAxis_ANameFiledUnderTheFourGroups_OrAPolicyGroupAlone_IsSingledOut_AndNoOtherGroupIs()
    {
        var names = NamesFiledUnderTheFourGroups().ToHashSet(StringComparer.Ordinal);
        var probed = ReadLineageEntries().Select(e => e.GetProperty("conceptId").GetString()!)
            .Concat(SnapshotFields().SelectMany(f => f.Groups))
            .Distinct(StringComparer.Ordinal);

        probed.Where(id => Real.Value.AreSingledOutBy([id], [])
                           != (names.Contains(id) || ReligiousOccupationGroups.ConceptIds.Contains(id)))
            .ShouldBeEmpty();
    }

    // "Välj alla" on a field puts every group of it on the axis. A whole field means the field, so a policy
    // group inside it is not singled out (senior-cto-advisor, 2026-10-26). The fields are found from the raw
    // snapshot, never named here.
    [Fact]
    public void AWholeFieldHoldingAPolicyGroup_IsNotSingledOut()
    {
        var fields = FieldsHoldingAPolicyGroup();
        fields.ShouldNotBeEmpty();

        foreach (var field in fields)
            Real.Value.AreSingledOutBy(field.Groups, []).ShouldBeFalse(field.ConceptId);
        Real.Value.AreSingledOutBy([.. fields.SelectMany(f => f.Groups)], []).ShouldBeFalse();
    }

    [Fact]
    public void AWholeFieldMinusOneOrdinaryGroup_SinglesOutThePolicyGroupItKeeps()
    {
        foreach (var field in FieldsHoldingAPolicyGroup())
        {
            var ordinary = field.Groups.First(g => !ReligiousOccupationGroups.ConceptIds.Contains(g));
            Real.Value.AreSingledOutBy([.. field.Groups.Where(g => g != ordinary)], []).ShouldBeTrue(field.ConceptId);
        }
    }

    [Fact]
    public void AWholeFieldPlusAReligiousName_IsSingledOut()
    {
        var name = NamesFiledUnderTheFourGroups().First();

        foreach (var field in FieldsHoldingAPolicyGroup())
        {
            Real.Value.AreSingledOutBy(field.Groups, [name]).ShouldBeTrue(field.ConceptId);
            Real.Value.AreSingledOutBy([.. field.Groups, name], []).ShouldBeTrue(field.ConceptId);
        }
    }

    // Coverage, not equality: a whole field stays a whole field beside a group of a field that holds no
    // religious group (senior-cto-advisor's own example, "all of Chefer plus Ekonomiassistenter").
    [Fact]
    public void AWholeFieldPlusAnOrdinaryGroupOfAnotherField_IsNotSingledOut()
    {
        var ordinary = SnapshotFields()
            .First(f => !f.Groups.Any(ReligiousOccupationGroups.ConceptIds.Contains))
            .Groups[0];

        foreach (var field in FieldsHoldingAPolicyGroup())
            Real.Value.AreSingledOutBy([.. field.Groups, ordinary], []).ShouldBeFalse(field.ConceptId);
    }

    // Declared unreachable: the committed snapshot holds every policy group, which
    // EveryPolicyGroup_IsAnOccupationGroupInTheTaxonomy_AndFilesAtLeastOneName pins, and a refresh trips the
    // version lock below. This asserts only how the guard degrades if that invariant broke: a policy group no
    // field holds is refused, never waved through as covered.
    [Fact]
    public void APolicyGroupNoFieldHolds_IsSingledOut()
    {
        var religious = new ReligiousOccupations(OccupationNameLineageLoader.Load(), new TaxonomySnapshotFile());

        foreach (var group in ReligiousOccupationGroups.ConceptIds)
            religious.AreSingledOutBy([group], []).ShouldBeTrue(group);
    }

    // A policy group of ANOTHER field, picked alone beside a whole field, was singled out.
    [Fact]
    public void AWholeFieldPlusAPolicyGroupOfAnotherFieldAlone_IsSingledOut()
    {
        var fields = FieldsHoldingAPolicyGroup();
        foreach (var field in fields)
        {
            var other = ReligiousOccupationGroups.ConceptIds.First(g => !field.Groups.Contains(g));
            Real.Value.AreSingledOutBy([.. field.Groups, other], []).ShouldBeTrue(field.ConceptId);
        }
    }

    // A whole field counts only on the group axis: the same ids sent as occupation-names cover nothing.
    [Fact]
    public void AWholeFieldOnTheNameAxis_IsSingledOut()
    {
        foreach (var field in FieldsHoldingAPolicyGroup())
            Real.Value.AreSingledOutBy([], field.Groups).ShouldBeTrue(field.ConceptId);
    }

    [Fact]
    public void EveryPolicyGroup_IsAnOccupationGroupInTheTaxonomy_AndFilesAtLeastOneName()
    {
        var groups = SnapshotFields().SelectMany(f => f.Groups).ToHashSet(StringComparer.Ordinal);
        var filing = ReadLineageEntries()
            .Select(e => e.GetProperty("groupConceptId").GetString())
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        foreach (var group in ReligiousOccupationGroups.ConceptIds)
        {
            groups.ShouldContain(group, $"{group} is not an occupation group in the taxonomy snapshot");
            filing.ShouldContain(group, $"the lineage artefact files no name under {group}");
        }
    }

    // Named offices, read by label from the artefact so the id is shown to be the office it stands for.
    // Kaplan is deprecated in both versions: a hand-edited URL still reaches it.
    [Theory]
    [InlineData("Imam")]
    [InlineData("Rabbin")]
    [InlineData("Präst, Svenska kyrkan")]
    [InlineData("Kyrkoherde")]
    [InlineData("Kaplan")]
    public void ANamedReligiousOffice_IsSingledOut(string label) =>
        Real.Value.AreSingledOutBy([], [ConceptIdOf(label)]).ShouldBeTrue();

    // Declared out of reach by senior-cto-advisor (2026-10-26): names filed under a secular group, including
    // Konsulent på stifts-/nationell nivå, whose replaced-by edges lead into Chefer och ledare inom
    // trossamfund. Not guarded, not assessed.
    [Theory]
    [InlineData("Församlingspedagog")]
    [InlineData("Kantor/Kyrkomusiker")]
    [InlineData("Konsulent på stifts-/nationell nivå")]
    public void ANameFiledUnderASecularGroup_IsNotSingledOut(string label) =>
        Real.Value.AreSingledOutBy([], [ConceptIdOf(label)]).ShouldBeFalse();

    // The lapse trigger (senior-cto-advisor 2026-10-26, point 1). The four groups and their fields were
    // confirmed against these versions; a taxonomy refresh fails here until its author re-confirms
    // ReligiousOccupationGroups against the new taxonomy and moves the pin.
    [Fact]
    public void TheFourGroups_WereConfirmedAgainstTheseTaxonomyVersions()
    {
        using var lineage = JsonDocument.Parse(Resource(LineageResourceName));
        using var snapshot = JsonDocument.Parse(Resource(SnapshotResourceName));

        lineage.RootElement.GetProperty("lineageVersion").GetString().ShouldBe("1");
        lineage.RootElement.GetProperty("taxonomyVersions").EnumerateArray().Select(v => v.GetString())
            .ShouldBe(["30", "31"]);
        snapshot.RootElement.GetProperty("taxonomyVersion").GetString().ShouldBe("30");
    }

    private sealed record Field(string ConceptId, IReadOnlyList<string> Groups);

    private static List<Field> FieldsHoldingAPolicyGroup() =>
        [.. SnapshotFields().Where(f => f.Groups.Any(ReligiousOccupationGroups.ConceptIds.Contains))];

    private static IEnumerable<string> NamesFiledUnderTheFourGroups() =>
        ReadLineageEntries()
            .Where(e => e.GetProperty("groupConceptId").GetString() is { } group
                        && ReligiousOccupationGroups.ConceptIds.Contains(group))
            .Select(e => e.GetProperty("conceptId").GetString()!)
            .Order(StringComparer.Ordinal);

    private static string ConceptIdOf(string label) =>
        ReadLineageEntries().Single(e => e.GetProperty("label").GetString() == label)
            .GetProperty("conceptId").GetString()!;

    private static List<Field> SnapshotFields()
    {
        using var snapshot = JsonDocument.Parse(Resource(SnapshotResourceName));
        return
        [
            .. snapshot.RootElement.GetProperty("occupationFields").EnumerateArray()
                .Select(f => new Field(
                    f.GetProperty("conceptId").GetString()!,
                    [.. f.GetProperty("occupationGroups").EnumerateArray().Select(g => g.GetProperty("conceptId").GetString()!)])),
        ];
    }

    private static List<JsonElement> ReadLineageEntries()
    {
        using var document = JsonDocument.Parse(Resource(LineageResourceName));
        return [.. document.RootElement.GetProperty("occupationNames").EnumerateArray().Select(e => e.Clone())];
    }

    private static Stream Resource(string name) =>
        typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly.GetManifestResourceStream(name)
        ?? throw new InvalidOperationException($"Embedded resource {name} is missing.");
}
