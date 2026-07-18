using Mediator;

namespace Jobbliggaren.Application.JobAds.Queries.GetRemoteAdCount;

/// <summary>
/// #551 PR-B D7 — the "Distans (N)" facet-hint count for the /jobb toolbar: how many
/// remote-classified ads match everything else the user has picked, ANSWERING "if you
/// switch to Distans, how many remote jobs remain".
/// <para>
/// <b>A boolean facet under Beslut-4 facet-exclusion, not a <c>FacetDimension</c>.</b>
/// A boolean has no concept-id key space, so it is NOT a GROUP-BY member of the facet
/// machinery — it is a single scalar via the total-count path
/// (<see cref="Abstractions.IJobAdSearchQuery.CountAsync"/>). Because remote is the
/// LOCATION dimension's boolean sub-axis (it unions with kommun/län in
/// <c>ApplyFilter</c>), faceting it excludes the WHOLE location dimension — so this
/// query deliberately does NOT even carry <c>Municipality</c>/<c>Region</c>
/// (structural exclusion). Counting <c>remote=true AND muni=X</c> would return ≈0
/// (remote ads are location-less) and lie about how many distansjobb exist. Only the
/// orthogonal non-location filters (yrke/anställningsform/omfattning/q) narrow it.
/// </para>
/// <para>
/// <b>Distinct from the notis/preview counters:</b> <c>GetMatchCountPreview</c>/
/// <c>GetMyMatchCount</c> use remote as part of the ACTUAL filter (union semantics,
/// equals the /jobb list's <c>TotalCount</c>); this hint is facet-EXCLUDED. Same
/// <c>JobAdFilterCriteria.Remote</c> mechanism, different input, different question.
/// Not <c>ICapturesRecentSearch</c> — a facet hint is not a search event (parity
/// <c>GetFacetCountsQuery</c>).
/// </para>
/// </summary>
public sealed record GetRemoteAdCountQuery(
    IReadOnlyList<string>? OccupationGroup = null,
    IReadOnlyList<string>? EmploymentType = null,
    IReadOnlyList<string>? WorktimeExtent = null,
    string? Q = null) : IQuery<RemoteAdCountDto>;
