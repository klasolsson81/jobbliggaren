using System.Text.Json;
using Jobbliggaren.Infrastructure.Taxonomy;
using Jobbliggaren.Infrastructure.TextAnalysis;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Matching.Taxonomy;

/// <summary>
/// #277 — the deterministic surface-grouping of ESCO + AF twin skill concept-ids into ONE chip,
/// exercised against the REAL embedded JobTech skill taxonomy + the real Swedish Snowball
/// analyzer (pure/in-process — no DB, parity <c>SkillResolverIntegrationTests</c>'s construction).
/// <para>
/// GOLDEN PROVENANCE (CLAUDE.md §5 — derive from the committed asset, NEVER hardcode concept-ids):
/// the C# twin set + the <c>scala</c>/<c>oracle-kunskaper</c> edge sets are READ LIVE from
/// <c>jobad-skill-taxonomy.v30.json</c> via the same provenance pattern as the integration test,
/// so an asset bump updates the expectation automatically rather than asserting a stale token.
/// </para>
/// <para>
/// The grouping helper lives on the internal <c>SkillTaxonomyIndex</c>, reachable here via
/// <c>InternalsVisibleTo("Jobbliggaren.Application.UnitTests")</c>. The pinned helper is
/// <c>GroupConceptIds</c> (saved/resolved ids → group those that share a preferred-label
/// surface). The invariants: ONE group for the "C#" twins with the ESCO/preferred canonical;
/// genuinely-distinct same-surface concepts (the AF "Scala, ..."/"Oracle ..." qualified concepts,
/// whose PREFERRED labels are unique single-owner surfaces) stay their OWN groups in reverse; no
/// crash; no dropped ids; every input id in exactly one group.
/// </para>
/// </summary>
public sealed class SkillSurfaceGroupingTests
{
    private const string SkillTaxonomyResource =
        "Jobbliggaren.Infrastructure.Taxonomy.jobad-skill-taxonomy.v30.json";

    // The same in-process construction the resolver/extractor use (one shared index).
    private static SkillTaxonomyIndex NewIndex() =>
        new(new LocalTextAnalyzer(new SnowballStemmer()));

    // ===============================================================
    // Saved/resolved ids grouped by their own preferred surface.
    // ===============================================================

    [Fact]
    public void GroupConceptIds_CSharpTwinPair_CollapsesToOneGroup_WithPreferredCanonical()
    {
        var concepts = ReadConcepts();
        var twinIds = ConceptIdsCarryingLiteral(concepts, "C#");
        twinIds.Count.ShouldBeGreaterThanOrEqualTo(2);

        var preferredCanonical = concepts
            .Single(c => string.Equals(c.PreferredLabel.Trim(), "C#", StringComparison.OrdinalIgnoreCase));

        var groups = NewIndex().GroupConceptIds(twinIds);

        groups.Count.ShouldBe(1,
            "De två 'C#'-tvillingarna delar ESCO-preferred-ytan 'C#' → EN chip.");
        groups[0].CanonicalConceptId.ShouldBe(preferredCanonical.ConceptId);
        groups[0].CanonicalLabel.ShouldBe("C#");
        groups[0].MemberConceptIds.ToHashSet(StringComparer.Ordinal).SetEquals(twinIds).ShouldBeTrue();

        AssertPartition(groups, twinIds);
    }

    [Theory]
    [InlineData("Scala")]
    [InlineData("Oracle")]
    public void GroupConceptIds_QualifiedSameStemConcepts_StayDistinctGroups_NoCollapse(string stem)
    {
        // The AF "qualified" concepts (e.g. "Scala, programmeringsspråk" / "Scala, affärssystem";
        // the "Oracle ..., <kategori>" family) carry the bare stem only as a SYNONYM — their
        // PREFERRED labels are UNIQUE single-owner literals. Reverse grouping uses each id's OWN
        // preferred-label surface, so these genuinely-distinct concepts must NOT collapse: each is
        // its own one-member group. This proves the grouping is by SHARED SURFACE, not a pairwise
        // same-stem heuristic. We deliberately EXCLUDE the bare ESCO concept (whose preferred label
        // IS the stem and therefore co-resolves the family) — its presence is a legitimate collapse,
        // covered by the C# twin test; here we pin the distinct-surface property.
        var concepts = ReadConcepts();
        var qualified = QualifiedConceptsWithUniquePreferredSurface(concepts, stem);
        qualified.Count.ShouldBeGreaterThanOrEqualTo(2,
            $"Förutsättning: minst två '{stem}, ...'-kvalificerade concepts med unik preferred-yta " +
            "(härled ur asseten, gissa aldrig).");

        var groups = NewIndex().GroupConceptIds(qualified);

        groups.Count.ShouldBe(qualified.Count,
            $"Varje '{stem}, ...'-kvalificerat concept har en UNIK preferred-yta → eget grupp " +
            "(ingen kollaps; grupperingen är per delad yta, ej per delad stam).");
        foreach (var g in groups)
            g.MemberConceptIds.Count.ShouldBe(1, "Unik preferred-yta → en-medlems-grupp.");

        AssertPartition(groups, qualified);
    }

    [Fact]
    public void GroupConceptIds_Empty_ReturnsEmpty_NoCrash()
    {
        NewIndex().GroupConceptIds([]).ShouldBeEmpty();
    }

    [Fact]
    public void GroupConceptIds_UnknownIds_BecomeOneMemberGroups_NeverDropped()
    {
        string[] unknown = ["skill_does_not_exist_1", "skill_does_not_exist_2"];

        var groups = NewIndex().GroupConceptIds(unknown);

        // Unknown ids carry no surface → each is its own one-member group (never dropped); the
        // partition invariant holds over the input set.
        groups.Count.ShouldBe(2);
        groups.SelectMany(g => g.MemberConceptIds).ToHashSet(StringComparer.Ordinal)
            .SetEquals(unknown).ShouldBeTrue();
        AssertPartition(groups, unknown);
    }

    [Fact]
    public void GroupConceptIds_MixedTwinsAndSingletons_PartitionsAllIds_DeterministicallyNoDrop()
    {
        var concepts = ReadConcepts();
        var twinIds = ConceptIdsCarryingLiteral(concepts, "C#");
        var scalaQualified = QualifiedConceptsWithUniquePreferredSurface(concepts, "Scala");

        // Twins + two distinct qualified singletons in one input set.
        var input = twinIds.Concat(scalaQualified).Distinct(StringComparer.Ordinal).ToList();

        var index = NewIndex();
        var groups = index.GroupConceptIds(input);

        // The twins collapse to one group; each qualified concept is its own group.
        var twinGroup = groups.Single(g =>
            g.MemberConceptIds.ToHashSet(StringComparer.Ordinal).SetEquals(twinIds));
        twinGroup.MemberConceptIds.Count.ShouldBe(twinIds.Count);
        groups.Count.ShouldBe(1 + scalaQualified.Count);

        AssertPartition(groups, input);

        // Determinism: a second call over the same input yields the same partition.
        var again = index.GroupConceptIds(input);
        again.Select(g => g.CanonicalConceptId).ShouldBe(groups.Select(g => g.CanonicalConceptId));
    }

    // ---------------------------------------------------------------
    // Partition invariant (#277): every input id appears in EXACTLY one group.
    // ---------------------------------------------------------------
    private static void AssertPartition(
        IReadOnlyList<SkillSurfaceGroup> groups, IReadOnlyCollection<string> input)
    {
        var emitted = groups.SelectMany(g => g.MemberConceptIds).ToList();
        var distinctInput = input.ToHashSet(StringComparer.Ordinal);
        emitted.Count.ShouldBe(distinctInput.Count, "Inget id får dupliceras eller droppas.");
        emitted.ToHashSet(StringComparer.Ordinal).SetEquals(distinctInput).ShouldBeTrue(
            "Varje input-id ska ligga i exakt en grupp (partition).");
    }

    // ---------------------------------------------------------------
    // Golden provenance — read the committed asset live (parity the integration test's
    // ReadSearchConcepts), derive the twin/edge sets, never hardcode concept-ids.
    // ---------------------------------------------------------------

    // Concept-ids carrying the trimmed literal (case-insensitive) as preferred label OR synonym
    // — the forward "surface owners" of the literal.
    private static List<string> ConceptIdsCarryingLiteral(IReadOnlyList<SkillConceptJson> concepts, string literal) =>
        concepts
            .Where(c => CarriesLiteral(c, literal))
            .Select(c => c.ConceptId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

    // The "<stem>, <kategori>"-qualified concepts whose PREFERRED label is a UNIQUE single-owner
    // literal (so reverse grouping keeps each as its own group). Excludes the bare ESCO concept
    // whose preferred label IS the stem.
    private static List<string> QualifiedConceptsWithUniquePreferredSurface(
        IReadOnlyList<SkillConceptJson> concepts, string stem)
    {
        var preferredOwnerCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in concepts)
        {
            var pl = c.PreferredLabel.Trim();
            if (pl.Length > 0)
                preferredOwnerCount[pl] = preferredOwnerCount.GetValueOrDefault(pl) + 1;
        }

        return concepts
            .Where(c =>
            {
                var pl = c.PreferredLabel.Trim();
                return pl.StartsWith(stem + ",", StringComparison.OrdinalIgnoreCase)
                    && preferredOwnerCount.GetValueOrDefault(pl) == 1;
            })
            .Select(c => c.ConceptId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
    }

    private static bool CarriesLiteral(SkillConceptJson c, string literal)
    {
        if (string.Equals(c.PreferredLabel.Trim(), literal, StringComparison.OrdinalIgnoreCase))
            return true;
        foreach (var s in c.Synonyms)
            if (string.Equals(s?.Trim(), literal, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static List<SkillConceptJson> ReadConcepts()
    {
        var asm = typeof(LocalTextAnalyzer).Assembly; // Infrastructure assembly
        using var stream = asm.GetManifestResourceStream(SkillTaxonomyResource);
        stream.ShouldNotBeNull(
            $"Skill-taxonomi-resursen '{SkillTaxonomyResource}' ska vara en <EmbeddedResource> " +
            "i Infrastructure-assemblyn.");

        using var doc = JsonDocument.Parse(stream!);
        var skills = doc.RootElement.GetProperty("skills");
        var list = new List<SkillConceptJson>(skills.GetArrayLength());
        foreach (var el in skills.EnumerateArray())
        {
            var synonyms = new List<string>();
            if (el.TryGetProperty("synonyms", out var syns) && syns.ValueKind == JsonValueKind.Array)
                foreach (var s in syns.EnumerateArray())
                    if (s.GetString() is { } str)
                        synonyms.Add(str);

            list.Add(new SkillConceptJson(
                el.GetProperty("conceptId").GetString()!,
                el.GetProperty("preferredLabel").GetString()!,
                synonyms));
        }
        return list;
    }

    private sealed record SkillConceptJson(string ConceptId, string PreferredLabel, IReadOnlyList<string> Synonyms);
}
