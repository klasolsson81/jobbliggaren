namespace JobbPilot.Application.JobAds.Abstractions;

/// <summary>
/// Filter-SPOT för jobbannons-sök (ADR 0039 Beslut 1, ADR 0062) — det enda
/// stället som definierar "vad gör en annons träffad": equality-filter per
/// taxonomi-dimension och q-FTS-hybrid. Tre konsumenter delar denna typ via
/// <see cref="IJobAdSearchQuery"/>: <c>ListJobAds</c> + <c>RunSavedSearch</c>
/// (via <see cref="JobAdSearchCriteria"/>) och <c>ListRecentSearches</c> (via
/// <see cref="IJobAdSearchQuery.CountAsync"/>) — de kan aldrig divergera i
/// filter-logik.
/// <para>
/// <b>ADR 0067 Beslut 1 (Platsbanken sök-paritet, Fas C1, Variant C) — yrke-
/// nivåbyte:</b> det primära yrke-filtret targetar nu <see cref="OccupationGroup"/>
/// (ssyk-level-4/yrkesgrupp, ~400) i stället för occupation-name. <see cref="Ssyk"/>
/// (occupation-name) BEVARAS i SPOT-formen — den matas av persisterad
/// <c>SearchCriteria.Ssyk</c>-VO (RunSavedSearch) och <c>RecentJobSearch.Ssyk</c>
/// (ListRecentSearches) vars rename är C2-bunden (VO-expansion) — men dess
/// explicita equality-gren är BORTTAGEN ur <c>JobAdSearchQuery.ApplyCriteria</c>
/// (no-op tills Fas E byter FE-picker + Fas C2 reverse-lookup-migrerar sparade
/// sökningar). occupation-name lever vidare som synonym-/recall-substrat på
/// q-FTS-vägen (orörd) — ingen recall förloras.
/// </para>
/// <para>
/// Alla listor är aldrig null — en tom lista betyder "inget filter" (handlern
/// normaliserar <c>null → []</c>, ADR 0042 Beslut B). <see cref="Q"/> är
/// null/whitespace = ingen fritextsökning. <b>Named arguments obligatoriskt</b>
/// vid konstruktion (fyra listor i rad = tyst-fel-fälla vid positionell mappning).
/// </para>
/// </summary>
public sealed record JobAdFilterCriteria(
    IReadOnlyList<string> OccupationGroup,
    IReadOnlyList<string> Municipality,
    IReadOnlyList<string> Region,
    IReadOnlyList<string> Ssyk,
    string? Q);
