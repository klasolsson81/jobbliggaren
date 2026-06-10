namespace JobbPilot.Application.JobAds.Abstractions;

/// <summary>
/// Facetterbar sök-dimension för <see cref="IJobAdSearchQuery.FacetCountsAsync"/>
/// (ADR 0067 Beslut 4 — Platsbanken sök-paritet Fas D1). Varje medlem mappar
/// 1:1 mot en filtrerbar STORED shadow-column i <c>JobAdSearchQuery</c> och mot
/// en lista i <see cref="JobAdFilterCriteria"/>.
/// <para>
/// <b>Scope (senior-cto-advisor 2026-06-10, VAL 1 = Variant A):</b> endast de tre
/// dimensioner som har populerad data OCH en equality-gren i <c>ApplyCriteria</c>
/// idag. Anställningsform/omfattning (B2-dims) UTESLUTS medvetet tills full
/// re-ingest populerat <c>employment_type_concept_id</c>/<c>worktime_extent_concept_id</c>
/// (~44k rader är NULL tills dess) — en facett mot NULL-data vore "falsk klar"
/// (CLAUDE.md §9.6, C1-CTO-dom c). De tillkommer additivt (non-breaking enum-append)
/// i samma PR som re-ingestens data, med GROUP BY-gren + Testcontainers-rad.
/// </para>
/// </summary>
public enum FacetDimension
{
    /// <summary>Yrkesgrupp (ssyk-level-4) — primärt yrke-filter
    /// (<c>OccupationGroupConceptId</c>).</summary>
    OccupationGroup,

    /// <summary>Kommun (<c>MunicipalityConceptId</c>).</summary>
    Municipality,

    /// <summary>Län (<c>RegionConceptId</c>).</summary>
    Region,
}
