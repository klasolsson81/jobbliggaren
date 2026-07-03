using Mediator;

namespace Jobbliggaren.Application.JobAds.Queries.GetMatchCountPreview;

/// <summary>
/// Epik #526 (ADR 0089) — den LIVE sök-preview-räknaren för matchnings-setup-modalen.
/// Räknar hur många aktiva annonser som matchar ett UTKAST av sök-facetter (yrke/ort/
/// anställningsform) medan användaren fyller i sin matchning, debouncat ~400 ms klient-side.
/// <para>
/// <b>Ren sök-preview (Z1, CTO-bind 2026-07-02 + Klas):</b> varje facett HARD-filtrerar
/// (som en <c>/jobb</c>-sökning) — därför gallrar varje val monotont, och ort+form fungerar
/// UTAN ett valt yrke ("alla jobb i Göteborg med viss anställning"). Utkastet mappas till en
/// <see cref="Abstractions.JobAdFilterCriteria"/> och räknas via den delade filter-SPOT:en
/// (<see cref="Abstractions.IJobAdSearchQuery.CountAsync"/>) — INGEN grad, INGEN profil, INGEN
/// per-användar-data. Talet är per konstruktion lika med den länkade <c>/jobb</c>-sökningens
/// <c>TotalCount</c> för samma facetter.
/// </para>
/// <para>
/// <b>Kompetenser ingår MEDVETET inte (Klas 2026-07-02):</b> skills är ingen Platsbanken-
/// sökfacett — de påverkar bara matchnings-KVALITETEN (grad), som surfas separat som
/// grad-taggar på <c>/jobb</c> + Översikts-notisen. Att tvinga in dem här skulle flippa talets
/// betydelse (sök-antal ↔ grad-antal). Därför bär utkastet bara de fyra sökbara dimensionerna.
/// </para>
/// <para>
/// <b>Systervägen <c>GetMyMatchCount</c> (ADR 0079, harmoniserad H2 2026-07-03):</b> den
/// räknar SAMMA sök-facett-fråga över den SPARADE profilen (hårda filter, samma SPOT) —
/// talen är per konstruktion identiska för samma val (Klas "samma siffra", ADR 0089).
/// </para>
/// <para>
/// INTE <c>ICapturesRecentSearch</c> — en live-räkning är ingen sökhändelse; auto-capture hade
/// skrivit en recent-search-rad per tangenttryckning (parity <c>GetFacetCountsQuery</c>).
/// </para>
/// </summary>
public sealed record GetMatchCountPreviewQuery(
    IReadOnlyList<string> OccupationGroups,
    IReadOnlyList<string> Regions,
    IReadOnlyList<string> Municipalities,
    IReadOnlyList<string> EmploymentTypes) : IQuery<MatchCountPreviewDto>;
