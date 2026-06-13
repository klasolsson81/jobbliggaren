namespace Jobbliggaren.Application.JobAds.Abstractions;

/// <summary>
/// Facetterbar sök-dimension för <see cref="IJobAdSearchQuery.FacetCountsAsync"/>
/// (ADR 0067 Beslut 4 — Platsbanken sök-paritet Fas D1). Varje medlem mappar
/// 1:1 mot en filtrerbar STORED shadow-column i <c>JobAdSearchQuery</c> och mot
/// en lista i <see cref="JobAdFilterCriteria"/>.
/// <para>
/// <b>ADR 0067 Beslut 6 (Fas B2, 2026-06-12):</b> Anställningsform/omfattning
/// (Klass 2-dims) tillkom (non-breaking enum-append) i samma PR som re-ingestad
/// data (~79% av Active populerad). Tidigare uteslutna tills data fanns — gaten
/// uppfylld empiriskt. Båda är ORTOGONALA dimensioner (egen lista exkluderas i
/// facetten, INTE hela en geo-union à la Municipality/Region).
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

    /// <summary>Anställningsform (<c>EmploymentTypeConceptId</c>) — ADR 0067 Beslut 6.</summary>
    EmploymentType,

    /// <summary>Omfattning/arbetstid (<c>WorktimeExtentConceptId</c>) — ADR 0067 Beslut 6.</summary>
    WorktimeExtent,
}
