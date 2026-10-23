using System.Text.Json;
using Jobbliggaren.Infrastructure.Taxonomy;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Taxonomy;

/// <summary>
/// The test oracle for the occupation-name index (#2198, ADR 0180): the committed
/// taxonomy-snapshot.json and the frozen occupation-name→ssyk-level-4 map, read straight from
/// their embedded resources by name. It shares no code with <see cref="OccupationNameIndex"/>,
/// so a test that compares the two measures the index rather than restating it.
/// </summary>
internal static class OccupationNameOracle
{
    private const string SnapshotResource = "Jobbliggaren.Infrastructure.Taxonomy.taxonomy-snapshot.json";

    private const string FrozenMapResource =
        "Jobbliggaren.Infrastructure.Persistence.Migrations.Resources." +
        "occupation-name-to-ssyk-level-4.v30.json";

    /// <summary>One expected matchable name: its label, its own concept id, and its group.</summary>
    internal sealed record ExpectedName(string NameLabel, string NameConceptId, string GroupConceptId, string GroupLabel);

    internal sealed record Expected(IReadOnlyList<ExpectedName> Names, IReadOnlyList<(string ConceptId, string Label)> Groups);

    /// <summary>
    /// The names in the read model's tree order (fields by label Ordinal, names within a field by
    /// label Ordinal), filtered to "the frozen map resolves it to a group the snapshot has", and
    /// every group once, its label the Ordinal minimum of its spellings.
    /// </summary>
    internal static async Task<Expected> LoadAsync()
    {
        using var snapshot = await ParseResourceAsync(SnapshotResource);
        using var map = await ParseResourceAsync(FrozenMapResource);

        var groupByName = map.RootElement.GetProperty("mappings").EnumerateObject()
            .ToDictionary(e => e.Name, e => e.Value.GetString()!, StringComparer.Ordinal);

        var fields = snapshot.RootElement.GetProperty("occupationFields").EnumerateArray()
            .Select(f => new
            {
                Label = f.GetProperty("label").GetString()!,
                Names = f.GetProperty("occupations").EnumerateArray()
                    .Select(o => (Id: o.GetProperty("conceptId").GetString()!, Label: o.GetProperty("label").GetString()!))
                    .OrderBy(o => o.Label, StringComparer.Ordinal)
                    .ToList(),
                Groups = f.GetProperty("occupationGroups").EnumerateArray()
                    .Select(g => (Id: g.GetProperty("conceptId").GetString()!, Label: g.GetProperty("label").GetString()!))
                    .OrderBy(g => g.Label, StringComparer.Ordinal)
                    .ToList(),
            })
            .OrderBy(f => f.Label, StringComparer.Ordinal)
            .ToList();

        var groupLabelById = fields.SelectMany(f => f.Groups)
            .GroupBy(g => g.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Label).Order(StringComparer.Ordinal).First(), StringComparer.Ordinal);

        var groups = fields.SelectMany(f => f.Groups)
            .Select(g => g.Id)
            .Distinct(StringComparer.Ordinal)
            .Select(id => (id, groupLabelById[id]))
            .ToList();

        var names = new List<ExpectedName>();
        foreach (var name in fields.SelectMany(f => f.Names))
        {
            if (groupByName.TryGetValue(name.Id, out var groupId) && groupLabelById.TryGetValue(groupId, out var groupLabel))
                names.Add(new ExpectedName(name.Label, name.Id, groupId, groupLabel));
        }

        return new Expected(names, groups);
    }

    private static async Task<JsonDocument> ParseResourceAsync(string resourceName)
    {
        var asm = typeof(OccupationNameIndex).Assembly;
        await using var stream = asm.GetManifestResourceStream(resourceName);
        stream.ShouldNotBeNull($"The embedded resource '{resourceName}' must exist in the Infrastructure assembly.");
        return await JsonDocument.ParseAsync(stream!);
    }
}
