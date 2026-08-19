using Jobbliggaren.Application.Common.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.RecentJobSearches.Queries.ListRecentSearches;

/// <summary>
/// ADR 0060 — list-projektion för auto-fångade RecentJobSearches per JobSeeker.
///
/// <para><b>IncludeCount</b> (default true): styr om <see cref="RecentJobSearchDto.CurrentCount"/>
/// + <see cref="RecentJobSearchDto.NewCount"/> beräknas. Sätts <c>false</c> av lättviktiga
/// konsumenter som bara behöver <c>Label</c> + <c>LastViewedAt</c> (t.ex. /oversikt-
/// Sammanfattning "Senaste sökning"-raden). Skippar N+1-COUNT-loopen (cap=20) som annars
/// kostar fan-out (ADR 0060 Beslut 4; TD-94:s per-count-rot är fixad). F6 P5 P4 svans-PR4 (2026-05-24,
/// Klas perf-feedback /oversikt 7-10s).</para>
/// </summary>
public sealed record ListRecentSearchesQuery(bool IncludeCount = true)
    : IQuery<IReadOnlyList<RecentJobSearchDto>>, IAuthenticatedRequest;
