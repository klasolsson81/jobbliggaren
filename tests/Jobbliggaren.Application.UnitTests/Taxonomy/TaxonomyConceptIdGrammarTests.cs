using System.Text.Json;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.SavedSearches;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Taxonomy;

/// <summary>
/// Fitness function: every conceptId in the SHIPPED taxonomy assets is a legal
/// search value under the grammar this system enforces.
///
/// <para><b>Why this exists.</b> <see cref="SearchCriteria"/> declares
/// <c>ConceptIdPattern</c> = <c>^[A-Za-z0-9_-]{1,32}\z</c> and every query
/// validator applies it at the entry point. Nothing, however, asserted that the
/// ids we actually SEED satisfy it. The two are written by different hands: the
/// pattern is domain code, while the ids arrive when someone regenerates the
/// snapshots off-repo and commits them as a multi-megabyte diff nobody reads
/// line by line (ADR 0043 Beslut B fixes that cadence as manual regeneration).
/// A refresh introducing a 33-character id, or one carrying <c>:</c>, would seed
/// cleanly and then 400 every search that used it — with green CI.</para>
///
/// <para><b>Why it also guards a frontend decision.</b> <c>/jobb</c> serialises
/// each filter axis as ONE query param with the values joined by <c>.</c>
/// (<c>web/jobbliggaren-web/src/lib/job-ads/search-params.ts</c>). That contract
/// is sound precisely because <c>.</c> lies OUTSIDE the charset above, so no
/// legal conceptId can contain one — whereas <c>-</c> lies inside it, which is
/// why the sibling surface's separator could not simply be reused. The frontend
/// owns the separator choice and pins it against this charset; this test owns
/// the other half — that the corpus really does obey the charset.</para>
///
/// <para>It asserts through <see cref="SearchCriteria.Create"/> rather than
/// against a copied regex, so it exercises the real production gate. A copied
/// pattern would pass while the real one drifted.</para>
/// </summary>
public sealed class TaxonomyConceptIdGrammarTests
{
    private static readonly string[] ResourceNames =
    [
        "Jobbliggaren.Infrastructure.Taxonomy.taxonomy-snapshot.json",
        "Jobbliggaren.Infrastructure.Taxonomy.klass2-taxonomy.json",
        "Jobbliggaren.Infrastructure.Taxonomy.occupation-substitutability.json",
        "Jobbliggaren.Infrastructure.Taxonomy.jobad-skill-taxonomy.v30.json",
    ];

    /// <summary>
    /// The separator `/jobb` joins axis values with. Declared here rather than
    /// imported because it lives in the frontend; the assertion below is what
    /// keeps the two in step, and it fails loudly if either side moves.
    /// </summary>
    private const char JobbAxisSeparator = '.';

    public static TheoryData<string> Resources()
    {
        var data = new TheoryData<string>();
        foreach (var name in ResourceNames) data.Add(name);
        return data;
    }

    [Theory]
    [MemberData(nameof(Resources))]
    public void ShippedTaxonomy_EveryConceptId_IsAcceptedBySearchCriteria(string resourceName)
    {
        var ids = ReadConceptIds(resourceName);

        // Non-vacuity: an empty set would satisfy every assertion below. The shape
        // of these files has changed before, and a silent zero here would retire
        // the guard without anyone noticing.
        ids.ShouldNotBeEmpty(
            $"no conceptIds were read out of {resourceName} — its shape changed, so this guard is measuring nothing");

        // Batched at the production cap: `Create` rejects more than
        // MaxConceptIds per axis, and that cap is not what is under test here.
        foreach (var batch in ids.Chunk(SearchCriteria.MaxConceptIds))
        {
            var result = SearchCriteria.Create(
                occupationGroup: batch,
                municipality: null,
                region: null,
                employmentType: null,
                worktimeExtent: null,
                employer: null,
                remote: false,
                q: null,
                sortBy: JobAdSortBy.PublishedAtDesc);

            result.IsSuccess.ShouldBeTrue(
                $"{resourceName} contains a conceptId the search gate rejects: {result.Error?.Message}. " +
                "Regenerating a taxonomy snapshot must not introduce ids outside " +
                "SearchCriteria.ConceptIdPattern — such an id seeds fine and then 400s every search that uses it.");
        }
    }

    [Fact]
    public void JobbAxisSeparator_IsOutsideTheConceptIdCharset()
    {
        // The frontend joins axis values on this character. If it were a legal
        // conceptId character the joined value would parse back as extra values,
        // silently widening the filter with the chip still showing. `-` IS legal
        // here, which is why /jobb could not reuse /foretag/sok's separator.
        var asId = JobbAxisSeparator.ToString();

        var result = SearchCriteria.Create(
            occupationGroup: [asId],
            municipality: null,
            region: null,
            employmentType: null,
            worktimeExtent: null,
            employer: null,
            remote: false,
            q: null,
            sortBy: JobAdSortBy.PublishedAtDesc);

        result.IsFailure.ShouldBeTrue(
            "the /jobb axis separator must be a character no legal conceptId can contain; " +
            "if the gate accepts it, joining an axis on it is ambiguous by contract");
    }

    private static List<string> ReadConceptIds(string resourceName)
    {
        var assembly = typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded taxonomy asset missing: {resourceName}. Check <EmbeddedResource> in Jobbliggaren.Infrastructure.csproj.");

        using var document = JsonDocument.Parse(stream);
        var ids = new List<string>();
        Collect(document.RootElement, ids);
        return ids.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>Every string under a <c>conceptId</c>-suffixed key, at any depth.</summary>
    private static void Collect(JsonElement element, List<string> into)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String
                        && property.Name.EndsWith("conceptId", StringComparison.OrdinalIgnoreCase))
                    {
                        into.Add(property.Value.GetString()!);
                    }
                    else
                    {
                        Collect(property.Value, into);
                    }
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) Collect(item, into);
                break;
        }
    }
}
