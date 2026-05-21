namespace JobbPilot.Application.JobAds.Abstractions;

/// <summary>
/// Filter-SPOT för jobbannons-sök (ADR 0039 Beslut 1, ADR 0062) — det enda
/// stället som definierar "vad gör en annons träffad": ssyk/region-equality
/// och q-FTS-hybrid. Tre konsumenter delar denna typ via
/// <see cref="IJobAdSearchQuery"/>: <c>ListJobAds</c> + <c>RunSavedSearch</c>
/// (via <see cref="JobAdSearchCriteria"/>) och <c>ListRecentSearches</c> (via
/// <see cref="IJobAdSearchQuery.CountAsync"/>) — de kan aldrig divergera i
/// filter-logik.
/// <para>
/// <see cref="Ssyk"/>/<see cref="Region"/> är aldrig null — en tom lista
/// betyder "inget filter" (handlern normaliserar <c>null → []</c>, ADR 0042
/// Beslut B). <see cref="Q"/> är null/whitespace = ingen fritextsökning.
/// </para>
/// </summary>
public sealed record JobAdFilterCriteria(
    IReadOnlyList<string> Ssyk,
    IReadOnlyList<string> Region,
    string? Q);
