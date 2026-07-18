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
    ///
    /// <para>
    /// <b>#551 — <paramref name="remote"/> is threaded here, NOT into the payload.</b> Remote is AF's own
    /// <c>remote=true</c> classification, harvested once per snapshot run — the response schema does not
    /// carry it per-ad (ADR 0067 Beslut 3, amended 2026-07-18), so a test states it as a separate
    /// constructor arg exactly like the ACL (<c>PlatsbankenJobSource.MapFacets</c>) does. It stays
    /// <see langword="bool"/>? with the PRESERVE reading: <see langword="null"/> (the default) = "the
    /// harvest did not speak" → <c>JobAd.SetSourcePayload</c> keeps the ad's current value, so a
    /// non-remote seed reads the <c>bool NOT NULL DEFAULT false</c> column. Pass <c>remote: true</c> to
    /// seed a remote ad.
    /// </para>
    /// </summary>
    internal static JobAdFacets From(
        string? ssyk = null,
        string? occupationGroup = null,
        string? municipality = null,
        string? region = null,
        string? employmentType = null,
        string? worktimeExtent = null,
        string? organizationNumber = null,
        bool? remote = null) =>
        new(ssykConceptId: ssyk,
            occupationGroupConceptId: occupationGroup,
            municipalityConceptId: municipality,
            regionConceptId: region,
            employmentTypeConceptId: employmentType,
            worktimeExtentConceptId: worktimeExtent,
            organizationNumber: organizationNumber,
            remote: remote);

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
    ///
    /// <para>
    /// <b>#551 — <paramref name="remote"/> rides ALONGSIDE the parsed payload, deliberately.</b> The
    /// remote/distans signal is NOT in <c>raw_payload</c> (it is AF's snapshot-harvest classification,
    /// ADR 0067 Beslut 3), so a read-path test that seeds a remote ad keeps building its payload as
    /// before and passes <c>remote: true</c> as a separate arg — the same split the production ACL makes.
    /// It defaults to <see langword="null"/> (PRESERVE — the non-remote column default), so every existing
    /// single-arg callsite is byte-for-byte unchanged.
    /// </para>
    /// </summary>
    internal static JobAdFacets FromPayload(string? rawPayload, bool? remote = null)
    {
        if (string.IsNullOrWhiteSpace(rawPayload))
            return remote is null
                ? JobAdFacets.None
                // The blank-payload path still has to carry an explicit remote verdict when one is given
                // (JobAdFacets.None is a shared readonly instance whose get-only props preclude a `with`).
                : new JobAdFacets(
                    ssykConceptId: null,
                    occupationGroupConceptId: null,
                    municipalityConceptId: null,
                    regionConceptId: null,
                    employmentTypeConceptId: null,
                    worktimeExtentConceptId: null,
                    organizationNumber: null,
                    remote: remote);

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
            organizationNumber: Nested(root, "employer", "organization_number"),
            // #551 — the eighth facet is NOT parsed from the payload; it is the caller's explicit verdict.
            remote: remote);
    }

    private static string? Nested(JsonElement root, string parent, string child) =>
        root.TryGetProperty(parent, out var node)
        && node.ValueKind == JsonValueKind.Object
        && node.TryGetProperty(child, out var leaf)
        && leaf.ValueKind == JsonValueKind.String
            ? leaf.GetString()
            : null;
}
