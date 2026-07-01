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
/// <b>ADR 0067 Beslut 1 (Fas C2, CTO-dom (e) 2026-06-09):</b> <c>Ssyk</c>-
/// fältet (occupation-name) är BORTTAGET — dess equality-gren togs redan i C1,
/// och C2-reverse-lookup-migrationen + VO-/entity-expansionen upplöste de två
/// persistens-bundna konsumenter som motiverade fältets överlevnad.
/// occupation-name lever vidare som synonym-/recall-substrat på q-FTS-vägen
/// (<c>SsykConceptId</c>-kolumnen + <c>IOccupationSynonymExpander</c> i
/// <c>JobAdSearchQuery</c> — orörda; ingen recall förloras).
/// </para>
/// <para>
/// Alla listor är aldrig null — en tom lista betyder "inget filter" (handlern
/// normaliserar <c>null → []</c>, ADR 0042 Beslut B). <see cref="Q"/> är
/// null/whitespace = ingen fritextsökning. <b>Named arguments obligatoriskt</b>
/// vid konstruktion (sex listor i rad = tyst-fel-fälla vid positionell mappning).
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
/// </summary>
public sealed record JobAdFilterCriteria(
    IReadOnlyList<string> OccupationGroup,
    IReadOnlyList<string> Municipality,
    IReadOnlyList<string> Region,
    IReadOnlyList<string> EmploymentType,
    IReadOnlyList<string> WorktimeExtent,
    IReadOnlyList<string> Employer,
    string? Q);
