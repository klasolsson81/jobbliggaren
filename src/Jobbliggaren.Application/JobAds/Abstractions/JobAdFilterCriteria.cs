namespace Jobbliggaren.Application.JobAds.Abstractions;

/// <summary>
/// Filter-SPOT för jobbannons-sök (ADR 0039 Beslut 1, ADR 0062) — det enda
/// stället som definierar "vad gör en annons träffad": equality-filter per
/// taxonomi-dimension och q-FTS-hybrid. Tre konsumenter delar denna typ via
/// <see cref="IJobAdSearchQuery"/>: <c>ListJobAds</c> + <c>RunSavedSearch</c>
/// (via <see cref="JobAdSearchCriteria"/>) och <c>ListRecentSearches</c> (via
/// <see cref="IJobAdSearchQuery.CountAsync"/>) — de kan aldrig divergera i
/// filter-logik.
/// <para>
/// <b>#2199 (ADR 0067 Amendment 2026-10-24):</b> <see cref="OccupationName"/> holds the
/// occupation-names (yrkesbenämningar) the user picked. It is the same Yrke dimension as
/// <see cref="OccupationGroup"/> at a finer grain: the two are OR-ed, and the search port expands
/// each pick through <see cref="OccupationNameLineage"/> so an ad coded with a deprecated name
/// still answers the name that replaced it. The /jobb list and its facet counts carry it, and since
/// #2199 PR 2 so do the persisted paths: a recent search's replay and a saved search's run read it
/// from the stored criteria. Matchning's criteria and the remote count pass it empty.
/// </para>
/// <para>
/// Alla listor är aldrig null — en tom lista betyder "inget filter" (handlern
/// normaliserar <c>null → []</c>, ADR 0042 Beslut B). <see cref="Q"/> är användarens
/// råa söktext, minus-ord inräknade; sökporten parsar den (#2143), och null/whitespace =
/// ingen fritextsökning. <b>Named arguments obligatoriskt</b>
/// vid konstruktion (listor i rad = tyst-fel-fälla vid positionell mappning).
/// </para>
/// <para>
/// <b>ADR 0067 Beslut 6 (Fas B2, 2026-06-12):</b> <see cref="EmploymentType"/>
/// (anställningsform) + <see cref="WorktimeExtent"/> (omfattning) tillkom — Klass 2
/// query-wiring mot re-ingestad data. Ortogonala dimensioner: enkel IN-equality,
/// AND mot allt annat (till skillnad mot Municipality/Region som geo-union:as).
/// </para>
/// <para>
/// <b>#311 D6 (följ arbetsgivare, ADR 0087, 2026-06-30):</b> <see cref="Employer"/>
/// (org.nr — den KANONISKA arbetsgivar-nyckeln, ingen fuzzy namn-matchning,
/// "Volvo×20"-fällan) tillkom. Speglar Klass 2 EXAKT: ortogonal dimension, enkel
/// IN-equality på den STORED generated <c>organization_number</c>-kolumnen, AND mot
/// allt annat (INTE geo-union). <b>PERSISTERAD (PR-2b C1, 2026-07-01):</b> PR-2:s
/// CONTAINED-seam är ersatt — Domän-VO:t <c>SearchCriteria</c> bär nu employer, så de
/// persisterade vägarna (<c>RunSavedSearchQueryHandler</c> +
/// <c>ListRecentSearchesQueryHandler</c>) passerar det verkliga värdet och reproducerar
/// arbetsgivar-filtret. (Rena facet-/count-vägar utan employer-facet, t.ex.
/// <c>GetFacetCounts</c>/<c>GetMyMatchCount</c>, passerar fortfarande medvetet
/// <c>Employer: []</c> — de har ingen employer-dimension.)
/// </para>
/// <para>
/// <b>#551 PR-B D5 (distans/remote, ADR 0076 #551-amendment):</b>
/// <see cref="Remote"/> är en BOOLESK location-sub-axel (inte en lista): när
/// <c>true</c> unionas den in i geo-predikatet (kommun ∨ län ∨ <c>remote</c>) i
/// <c>ApplyFilter</c> mot annonsens <c>remote</c>-kolumn (AF:s distans-klassning,
/// PR-A). <c>false</c> ⇒ INGET remote-villkor emitteras (byte-identisk SQL mot
/// pre-#551). Obligatorisk (ingen default) — som list-axlarna tvingar den varje
/// konstruktions-site att ta ställning (transpositions-fällan ovan). Driver den
/// USER-VALDA facetten; annonsens flagga driver grad-override:n separat (F1 —
/// scorern läser ALDRIG användarens remote-preferens; ADR 0079 never-grade-coupled).
/// </para>
/// </summary>
public sealed record JobAdFilterCriteria(
    IReadOnlyList<string> OccupationGroup,
    IReadOnlyList<string> OccupationName,
    IReadOnlyList<string> Municipality,
    IReadOnlyList<string> Region,
    IReadOnlyList<string> EmploymentType,
    IReadOnlyList<string> WorktimeExtent,
    IReadOnlyList<string> Employer,
    bool Remote,
    string? Q);
