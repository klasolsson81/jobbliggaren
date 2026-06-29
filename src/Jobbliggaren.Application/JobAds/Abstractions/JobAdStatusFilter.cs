namespace Jobbliggaren.Application.JobAds.Abstractions;

/// <summary>
/// #383 (CTO-bind <c>cto-7f3a9c2e1b4d8a6f</c>, Approach B) — den PER-ANVÄNDAR-
/// status-predikaten för <c>/jobb</c>-listan (sparade / ansökta / dölj ansökta).
/// RUNTIME-KONTEXT, INTE en filter-identitets-dimension: den ingår ALDRIG i
/// <see cref="JobAdFilterCriteria"/> (den anonymt cachebara SPOT:en som
/// ListJobAds / RunSavedSearch / ListRecentSearches delar, ADR 0039 Beslut 1)
/// eller <c>ICapturesRecentSearch</c> — en personlig "visa bara mina sparade"-vy
/// får aldrig förorena den anonyma, cachebara sök-identiteten (paritet
/// MatchGrades/IncludeRelated).
/// <para>
/// Appliceras som <c>EXISTS</c> / <c>NOT EXISTS</c>-staplar i
/// <see cref="IPerUserJobAdSearchQuery"/> ENBART (aldrig den delade
/// <c>JobAdSearchComposition.ApplyFilter</c>): den per-användar-vägen bär redan
/// seekern, så status-WHERE:t bor där. <c>EXISTS</c> (VO==VO-kolumnjämförelse),
/// INTE <c>IN</c> (<c>List&lt;VO&gt;.Contains</c> 500:ar i översättningen —
/// <c>ef_strongly_typed_vo_contains</c>-lärdomen, CI 2026-05-23). EXISTS ärver
/// dessutom soft-delete-<c>HasQueryFilter</c> på <c>Application</c> automatiskt;
/// <c>SavedJobAd</c> är hard-delete (en rad finns iff annonsen är sparad nu).
/// </para>
/// <list type="bullet">
/// <item><see cref="SavedOnly"/> ∨ <see cref="AppliedOnly"/> = UNION (OR) — samma
/// sätt som status-batchen (ADR 0063) exponerar de två mängderna.</item>
/// <item><see cref="HideApplied"/> = <c>NOT EXISTS</c> (ansökt). Giltig TILLSAMMANS
/// med <see cref="SavedOnly"/> ("sparade jag inte sökt ännu"); ömsesidigt
/// uteslutande med <see cref="AppliedOnly"/> (självmotsägande — validatorn 400:ar
/// den kombinationen).</item>
/// </list>
/// </summary>
public readonly record struct JobAdStatusFilter(
    bool SavedOnly,
    bool AppliedOnly,
    bool HideApplied)
{
    /// <summary>Ingen status-predikat (match-only-/anon-list-vägarna passerar denna).</summary>
    public static JobAdStatusFilter None => default;

    /// <summary>Sann när minst en status-predikat smalnar resultatmängden.</summary>
    public bool IsActive => SavedOnly || AppliedOnly || HideApplied;
}
