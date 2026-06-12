using JobbPilot.Application.JobAds.Abstractions;
using Mediator;

namespace JobbPilot.Application.JobAds.Queries.GetFacetCounts;

/// <summary>
/// ADR 0067 Beslut 4 (Fas E2c) — per-option facet-counts för en dimension
/// givet aktuellt filterval. Returnerar rå concept-id → count (label-resolution
/// är FE-/taxonomi-ansvar, ADR 0043). INGEN Total-medlem — en total ur det
/// facett-exkluderade kriteriet vore semantiskt fel (SUM ≠ list-totalCount);
/// "Visa N annonser"-talet ägs av <c>PagedResult.TotalCount</c> (SPOT,
/// E2c-architect §1).
/// <para>
/// Implementerar MEDVETET INTE <c>ICapturesRecentSearch</c> — en
/// facett-räkning är ingen sökhändelse; auto-capture hade skrivit en
/// recent-search-rad per popover-toggle (E2c-architect §2).
/// </para>
/// </summary>
public sealed record GetFacetCountsQuery(
    FacetDimension Dimension,
    IReadOnlyList<string>? OccupationGroup = null,
    IReadOnlyList<string>? Municipality = null,
    IReadOnlyList<string>? Region = null,
    // ADR 0067 Beslut 6 (Fas B2) — Klass 2-filterkontext måste kunna anges så
    // facett-counten för en annan dimension reflekterar aktiva employment/
    // worktime-filter (annars räknar facetten mot en annan WHERE än listan).
    IReadOnlyList<string>? EmploymentType = null,
    IReadOnlyList<string>? WorktimeExtent = null,
    string? Q = null) : IQuery<IReadOnlyDictionary<string, int>>;
