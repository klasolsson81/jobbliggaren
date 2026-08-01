using System.Text.Json;
using Jobbliggaren.Application.JobAds.Queries.ListJobAds;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.SavedSearches;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Taxonomy;

/// <summary>
/// Fitness function: every conceptId in the SHIPPED taxonomy assets is a legal
/// search value under the grammar the read path enforces.
///
/// <para><b>Why this exists.</b> The conceptId grammar
/// (<c>^[A-Za-z0-9_-]{1,32}\z</c>) is applied by the query validators at every
/// entry point, but nothing asserted that the ids we actually SEED satisfy it.
/// The two are written by different hands: the pattern is our code, while the ids
/// arrive when someone regenerates the snapshots off-repo and commits them as a
/// multi-megabyte diff nobody reads line by line (ADR 0043 Beslut B fixes that
/// cadence as manual regeneration). A refresh introducing a 33-character id, or
/// one carrying <c>:</c>, would seed cleanly and then 400 every search that used
/// it — with green CI.</para>
///
/// <para><b>Why it also guards a frontend decision.</b> <c>/jobb</c> serialises
/// each filter axis as ONE query param with the values joined by <c>.</c>
/// (<c>web/jobbliggaren-web/src/lib/job-ads/search-params.ts</c>). That contract
/// is sound precisely because <c>.</c> lies OUTSIDE the charset above, so no
/// legal conceptId can contain one — whereas <c>-</c> lies inside it, which is
/// why the sibling surface's separator could not be reused. The frontend owns the
/// separator CHOICE and pins it against this charset (a backend guard cannot
/// catch a bad choice, since <c>-</c> passes every backend gate); this test owns
/// the other half — that the corpus really does obey the charset.</para>
///
/// <para><b>Which seam, precisely.</b> The pattern exists as twelve independent
/// literals across eleven files in Domain and Application (measured 2026-08-01;
/// `SetMatchPreferencesCommandValidator` carries two, and a thirteenth is frozen
/// inside migration `20260609214512_C2SearchParityReverseLookupAndRecentExpansion`),
/// so no single call site is "the" production gate and this test does not claim
/// one. It
/// asserts through TWO, which matter for different reasons:
/// <see cref="ListJobAdsQueryValidator"/> is what a <c>/jobb</c> search actually
/// hits, where a rejection is the 400 named above; and
/// <see cref="SearchCriteria"/> is the capture/persistence gate, where a
/// rejection is instead a silent no-capture.</para>
///
/// <para>The two are NOT equivalent, and <b>neither subsumes the other</b>.
/// <c>Create</c> normalises (trims, drops whitespace-only) BEFORE regexing, so it
/// accepts elements with surrounding whitespace that the validator — which
/// regexes the raw element — rejects. In the other direction <c>Create</c> is the
/// stricter one: it enforces an empty-criteria invariant and a <c>QMinLength</c>
/// that #831 deliberately removed from the read path, so it rejects queries the
/// validator passes. They are incomparable, which is why passing one says nothing
/// about the other.</para>
///
/// <para>Both are asserted because the two literals are independent copies of one
/// grammar. Today they are byte-identical, so on THIS corpus the second assertion
/// detects nothing the first does not — every id here is whitespace-free, which is
/// exactly the case the trim gap cannot reach. What it buys is DRIFT: a one-sided
/// tightening (Domain <c>{1,32}</c> to <c>{1,12}</c>) would go red here and nowhere
/// else (dotnet-architect, #1144).</para>
/// </summary>
public sealed class TaxonomyConceptIdGrammarTests
{
    private const string ResourcePrefix = "Jobbliggaren.Infrastructure.Taxonomy.";

    /// <summary>
    /// The separator `/jobb` joins axis values with. Declared here rather than
    /// imported because it lives in the frontend; the assertion below is what
    /// keeps the two in step, and it fails loudly if either side moves.
    /// </summary>
    private const char JobbAxisSeparator = '.';

    /// <summary>
    /// Discovered from the assembly rather than hand-listed: a hand-written list
    /// silently leaves a FIFTH taxonomy asset unguarded the day one is added,
    /// which is the same "guard narrower than its stated subject" failure this
    /// file exists to prevent (dotnet-architect, #1144).
    /// </summary>
    private static string[] ResourceNames() =>
        typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly
            .GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                     && n.EndsWith(".json", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

    public static TheoryData<string> Resources()
    {
        var data = new TheoryData<string>();
        foreach (var name in ResourceNames()) data.Add(name);
        return data;
    }

    [Fact]
    public void EveryEmbeddedTaxonomyAsset_IsDiscovered()
    {
        // Non-vacuity for the DISCOVERY itself: if the prefix or the embedding
        // changed, every theory below would simply not run, and the suite would
        // stay green while measuring nothing.
        ResourceNames().Length.ShouldBeGreaterThanOrEqualTo(
            4,
            "the four known taxonomy assets must be discovered as embedded resources — " +
            "if this drops, the grammar guard below runs on fewer files than it claims");
    }

    /// <summary>
    /// Pins how MUCH the collector reaches, because the grammar assertions below
    /// cannot: every id in the corpus is currently legal, so a collector that
    /// silently reads half of them passes them all. That is exactly what happened
    /// — the first version read 23 937 of 23 968 and nothing went red.
    ///
    /// <para>Floors, not equalities: the snapshots are regenerated on purpose and
    /// growth must not fail the build, while a DROP means the collector's reach
    /// shrank and the grammar guard quietly stopped covering the space.</para>
    /// </summary>
    [Fact]
    public void Collector_ReachesTheWholeCorpus()
    {
        var perFile = ResourceNames()
            .ToDictionary(name => name, name => ReadConceptIds(name).Count, StringComparer.Ordinal);

        var substitutability = perFile.Single(kv => kv.Key.Contains("occupation-substitutability", StringComparison.Ordinal));
        // 224 = 193 under `sourceConceptId` + 31 that appear ONLY inside the
        // `relatedConceptIds` ARRAYS. This number is the whole point of the file:
        // reading 193 here is the bug, and it is invisible to every other test.
        substitutability.Value.ShouldBeGreaterThanOrEqualTo(
            224,
            "the collector is missing ids that live only inside `relatedConceptIds` arrays");

        perFile.Values.Sum().ShouldBeGreaterThanOrEqualTo(
            23_968,
            "either the collector's reach shrank or the snapshot legitimately did — " +
            "verify the collector against the file BEFORE lowering this number, because " +
            "lowering it is also the cheapest way to re-open the blindness it was written for");
    }

    [Theory]
    [MemberData(nameof(Resources))]
    public void ShippedTaxonomy_EveryConceptId_PassesTheSearchReadPath(string resourceName)
    {
        var ids = ReadConceptIds(resourceName);

        ids.ShouldNotBeEmpty(
            $"no conceptIds were read out of {resourceName} — its shape changed, so this guard is measuring nothing");

        var validator = new ListJobAdsQueryValidator();

        // Batched at the production cap: both gates reject more than
        // MaxConceptIds per axis, and that cap is not what is under test here.
        foreach (var batch in ids.Chunk(SearchCriteria.MaxConceptIds))
        {
            var readPath = validator.Validate(new ListJobAdsQuery(OccupationGroup: batch));
            readPath.IsValid.ShouldBeTrue(
                $"{resourceName} contains a conceptId the SEARCH READ PATH rejects: " +
                $"{string.Join("; ", readPath.Errors.Select(e => e.ErrorMessage))}. " +
                "Regenerating a taxonomy snapshot must not introduce ids outside the conceptId " +
                "grammar — such an id seeds fine and then 400s every search that uses it.");

            var capture = SearchCriteria.Create(
                occupationGroup: batch,
                municipality: null,
                region: null,
                employmentType: null,
                worktimeExtent: null,
                employer: null,
                remote: false,
                q: null,
                sortBy: JobAdSortBy.PublishedAtDesc);
            capture.IsSuccess.ShouldBeTrue(
                $"{resourceName} contains a conceptId the CAPTURE/PERSISTENCE gate rejects: " +
                $"{capture.Error?.Message}. A rejection here is a silent no-capture, not a 400.");
        }
    }

    [Fact]
    public void JobbAxisSeparator_IsRejectedByTheConceptIdGrammar()
    {
        // The frontend joins axis values on this character. If it were a legal
        // conceptId character the joined value would parse back as extra values,
        // silently widening the filter with the chip still showing. `-` IS legal
        // here, which is why /jobb could not reuse /foretag/sok's separator.
        var result = new ListJobAdsQueryValidator()
            .Validate(new ListJobAdsQuery(OccupationGroup: [JobbAxisSeparator.ToString()]));

        result.IsValid.ShouldBeFalse(
            "the /jobb axis separator must be a character no legal conceptId can contain; " +
            "if the read path accepts it, joining an axis on it is ambiguous by contract");

        // WHY it was rejected, not merely THAT it was. A whitespace separator
        // would also be rejected — by emptying the list, not by the charset — and
        // this test would then pass for a reason that says nothing about
        // ambiguity (code-reviewer, #1144).
        result.Errors.ShouldContain(
            e => e.PropertyName.Contains("OccupationGroup", StringComparison.Ordinal),
            "the rejection must come from the conceptId charset rule on the axis itself");
    }

    private static List<string> ReadConceptIds(string resourceName)
    {
        var assembly = typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded taxonomy asset missing: {resourceName}.");

        using var document = JsonDocument.Parse(stream);
        var ids = new List<string>();
        Collect(document.RootElement, keyIsConceptId: false, ids);
        return ids.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Every string under a <c>conceptId</c>/<c>conceptIds</c>-suffixed key, at
    /// any depth, INCLUDING bare strings inside such an array.
    ///
    /// <para>The array case is not hypothetical. <c>occupation-substitutability.json</c>
    /// carries ids under both <c>sourceConceptId</c> (a string) and
    /// <c>relatedConceptIds</c> (an array of strings), and 31 of its ids appear
    /// ONLY in the latter. An earlier version matched the singular key and had no
    /// branch for a string array element, so it read 193 of that file's 224 ids
    /// and reported a corpus of 23 937 against a real 23 968. The non-vacuity
    /// assertion could not see it: 193 is greater than zero. Those ids are seeded
    /// as <c>TaxonomyRelation.RelatedConceptId</c> and consumed through
    /// <c>?relaterade=on</c>, so they sit squarely inside this guard's stated
    /// subject.</para>
    /// </summary>
    private static void Collect(JsonElement element, bool keyIsConceptId, List<string> into)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                if (keyIsConceptId) into.Add(element.GetString()!);
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Collect(property.Value, IsConceptIdKey(property.Name), into);
                }
                break;
            case JsonValueKind.Array:
                // The key's meaning carries into its elements: `relatedConceptIds`
                // names what the ARRAY holds, not what each element is called.
                foreach (var item in element.EnumerateArray()) Collect(item, keyIsConceptId, into);
                break;
        }
    }

    private static bool IsConceptIdKey(string name) =>
        name.EndsWith("conceptId", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith("conceptIds", StringComparison.OrdinalIgnoreCase);
}
