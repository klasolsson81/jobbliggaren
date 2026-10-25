using System.Text;
using System.Text.Json;
using Jobbliggaren.Infrastructure.Taxonomy;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Taxonomy;

/// <summary>
/// #2199 — a malformed occupation-name lineage never becomes a running host: each shape below goes
/// through the loader's real path and throws.
/// </summary>
public sealed class OccupationNameLineageLoaderTests
{
    [Fact]
    public void LoadFrom_AWellFormedStream_Loads()
    {
        var lineage = OccupationNameLineageLoader.LoadFrom(Stream(Valid()));

        lineage.Resolve("old_A", "grp_1").ShouldBe("new_A");
        lineage.CurrentConceptIds.ShouldBe(["new_A"]);
    }

    [Fact]
    public void LoadFrom_TheJsonNull_Throws() =>
        Should.Throw<InvalidOperationException>(() => OccupationNameLineageLoader.LoadFrom(new MemoryStream("null"u8.ToArray())))
            .Message.ShouldContain("null");

    [Fact]
    public void LoadFrom_NotJson_Throws() =>
        Should.Throw<JsonException>(() => OccupationNameLineageLoader.LoadFrom(new MemoryStream("not json"u8.ToArray())));

    [Theory]
    [InlineData("""{"taxonomyVersions":["31"],"occupationNames":[{"conceptId":"a","groupConceptId":"g","currentIn":["31"],"replacedByConceptIds":[]}]}""", "lineageVersion")]
    [InlineData("""{"lineageVersion":"1","taxonomyVersions":[],"occupationNames":[{"conceptId":"a","groupConceptId":"g","currentIn":[],"replacedByConceptIds":[]}]}""", "taxonomyVersions")]
    [InlineData("""{"lineageVersion":"1","taxonomyVersions":["31"],"occupationNames":[]}""", "no occupation-names")]
    [InlineData("""{"lineageVersion":"1","taxonomyVersions":["31"],"occupationNames":[{"conceptId":"a.b","groupConceptId":"g","currentIn":["31"],"replacedByConceptIds":[]}]}""", "a.b")]
    [InlineData("""{"lineageVersion":"1","taxonomyVersions":["31"],"occupationNames":[{"conceptId":"a","groupConceptId":"g g","currentIn":["31"],"replacedByConceptIds":[]}]}""", "g g")]
    [InlineData("""{"lineageVersion":"1","taxonomyVersions":["31"],"occupationNames":[{"conceptId":"a","groupConceptId":"g","currentIn":["32"],"replacedByConceptIds":[]}]}""", "32")]
    [InlineData("""{"lineageVersion":"1","taxonomyVersions":["31"],"occupationNames":[{"conceptId":"a","groupConceptId":null,"currentIn":["31"],"replacedByConceptIds":[]}]}""", "no group")]
    [InlineData("""{"lineageVersion":"1","taxonomyVersions":["31"],"occupationNames":[{"conceptId":"a","groupConceptId":"g","currentIn":[],"replacedByConceptIds":[""]}]}""", "replacement of a")]
    public void LoadFrom_AMalformedFile_ThrowsNamingTheDefect(string json, string named) =>
        Should.Throw<InvalidOperationException>(() => OccupationNameLineageLoader.LoadFrom(Stream(json)))
            .Message.ShouldContain(named);

    [Theory]
    [InlineData("""{"lineageVersion":"1","taxonomyVersions":["31"],"occupationNames":[{"conceptId":"a","groupConceptId":"g","currentIn":[],"replacedByConceptIds":["missing"]}]}""", "missing")]
    [InlineData("""{"lineageVersion":"1","taxonomyVersions":["31"],"occupationNames":[{"conceptId":"a","groupConceptId":"g","currentIn":[],"replacedByConceptIds":["b"]},{"conceptId":"b","groupConceptId":"g","currentIn":[],"replacedByConceptIds":["a"]}]}""", "cycle")]
    [InlineData("""{"lineageVersion":"1","taxonomyVersions":["31"],"occupationNames":[{"conceptId":"a","groupConceptId":"g","currentIn":["31"],"replacedByConceptIds":[]},{"conceptId":"a","groupConceptId":"g","currentIn":["31"],"replacedByConceptIds":[]}]}""", "twice")]
    public void LoadFrom_ABrokenGraph_Throws(string json, string named) =>
        Should.Throw<ArgumentException>(() => OccupationNameLineageLoader.LoadFrom(Stream(json)))
            .Message.ShouldContain(named);

    private static string Valid() =>
        """
        {"lineageVersion":"1","taxonomyVersions":["30","31"],"occupationNames":[
          {"conceptId":"new_A","groupConceptId":"grp_1","currentIn":["31"],"replacedByConceptIds":[]},
          {"conceptId":"old_A","groupConceptId":"grp_1","currentIn":[],"replacedByConceptIds":["new_A"]}
        ]}
        """;

    private static MemoryStream Stream(string json) => new(Encoding.UTF8.GetBytes(json));
}
