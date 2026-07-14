using System.Text.Json;
using Jobbliggaren.Domain.JobAds;

namespace Jobbliggaren.TestSupport;

/// <summary>
/// #841 — test-only convenience over <see cref="JobAdFacets"/>. Linked into every test project that
/// seeds an imported <see cref="JobAd"/> (see the <c>Compile Include</c> items in the test csproj files).
///
/// <para>
/// <b>Why this exists, and why the production constructor does NOT get optional parameters.</b>
/// <see cref="JobAdFacets"/> demands all seven arguments precisely so the ACL cannot silently omit one:
/// a new facet added to the record breaks <c>PlatsbankenJobSource.MapFacets</c> at compile time, which is
/// the entire point. Giving the production type defaults would trade that guarantee for brevity. Tests
/// have the opposite need — most seed one or two facets and care about nothing else — so the ergonomics
/// live HERE, on the test side of the boundary, where a forgotten facet costs nothing.
/// </para>
///
/// <para>
/// <b>Seed the payload and the facets from the SAME variables.</b> Before #841 the seven columns were
/// derived by Postgres from <c>raw_payload</c>, so a test only had to build the JSON. They are now written
/// by C#, so a test must state them — and if a test seeds a payload saying "Stockholm" while passing
/// facets saying "Göteborg", the row is simply wrong. Pass the same locals to both. (Production cannot
/// drift this way: <c>JobAd.SetSourcePayload</c> writes the payload and the facets atomically, and the ACL
/// parses both from one <c>JobTechHit</c>.)
/// </para>
/// </summary>
internal static class TestFacets
{
    /// <summary>The named absence — an ad with no source facets (parity <see cref="JobAdFacets.None"/>).</summary>
    internal static JobAdFacets None => JobAdFacets.None;

    /// <summary>
    /// Builds facets from the subset a test actually cares about; everything unnamed is <c>null</c>, which
    /// is what a payload lacking that key would have produced under the old generated columns.
    /// </summary>
    internal static JobAdFacets From(
        string? ssyk = null,
        string? occupationGroup = null,
        string? municipality = null,
        string? region = null,
        string? employmentType = null,
        string? worktimeExtent = null,
        string? organizationNumber = null) =>
        new(ssykConceptId: ssyk,
            occupationGroupConceptId: occupationGroup,
            municipalityConceptId: municipality,
            regionConceptId: region,
            employmentTypeConceptId: employmentType,
            worktimeExtentConceptId: worktimeExtent,
            organizationNumber: organizationNumber);

    /// <summary>
    /// Reads the seven facets out of a seeded <c>raw_payload</c>, along the exact JSON paths the ACL uses.
    ///
    /// <para>
    /// <b>This is the faithful translation of what Postgres used to do for these tests, and that is
    /// precisely why it exists.</b> Before #841 a test only had to build the payload JSON; the seven STORED
    /// generated columns then appeared by themselves. Dozens of search / matching / attribution tests are
    /// written that way, and their subject is the READ path — not ingest. Rewriting each of them to restate
    /// its facets by hand would have risked silently weakening tests that are not about facets at all
    /// (a mistyped facet in a negative-assertion test still passes, for the wrong reason).
    /// </para>
    ///
    /// <para>
    /// <b>It cannot rot silently.</b> If a path here were wrong the facet would be null, the column would
    /// be empty, and every test that filters on it goes RED — the parser is checked by the suite that uses
    /// it. And it does not weaken the production guarantee: the ACL's own path knowledge
    /// (<c>PlatsbankenJobSource.MapFacets</c>) is pinned independently against a real JobTech payload by
    /// <c>PlatsbankenJobSourceFacetMappingTests</c>. Nothing in <c>src/</c> parses a payload for facets
    /// except the ACL.
    /// </para>
    ///
    /// <para>
    /// Tests whose subject IS the facets (ingest, purge survival, the empty-string invariant) pass them
    /// explicitly via <see cref="From"/> instead.
    /// </para>
    /// </summary>
    internal static JobAdFacets FromPayload(string? rawPayload)
    {
        if (string.IsNullOrWhiteSpace(rawPayload))
            return JobAdFacets.None;

        using var doc = JsonDocument.Parse(rawPayload);
        var root = doc.RootElement;

        return new JobAdFacets(
            ssykConceptId: Nested(root, "occupation", "concept_id"),
            occupationGroupConceptId: Nested(root, "occupation_group", "concept_id"),
            municipalityConceptId: Nested(root, "workplace_address", "municipality_concept_id"),
            regionConceptId: Nested(root, "workplace_address", "region_concept_id"),
            employmentTypeConceptId: Nested(root, "employment_type", "concept_id"),
            // NAME GAP (mirrors the ACL and the retired SQL): the worktime-extent facet reads the
            // working_hours_type key. Pointing this at "worktime_extent" would yield a silently
            // always-null column — which is what every Klass 2 filter test would then catch.
            worktimeExtentConceptId: Nested(root, "working_hours_type", "concept_id"),
            organizationNumber: Nested(root, "employer", "organization_number"));
    }

    private static string? Nested(JsonElement root, string parent, string child) =>
        root.TryGetProperty(parent, out var node)
        && node.ValueKind == JsonValueKind.Object
        && node.TryGetProperty(child, out var leaf)
        && leaf.ValueKind == JsonValueKind.String
            ? leaf.GetString()
            : null;
}
