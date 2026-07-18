using Jobbliggaren.Domain.JobAds;

namespace Jobbliggaren.Application.RecentJobSearches.Common;

/// <summary>
/// Opt-in markör (ADR 0060) — queries som ska auto-capture:as till
/// RecentJobSearches när authenticated user kör sökning. Behaviorn
/// <c>RecentJobSearchCaptureBehavior</c> kör no-op för meddelanden utan
/// markören (paritet med <c>IRequiresFieldEncryptionKey</c>-mönstret).
///
/// <para>Interface exponerar de fält som tillsammans definierar filter-identitet
/// (Q, OccupationGroup, Municipality, Region, EmploymentType, WorktimeExtent,
/// Employer, Remote, SortBy — Fas C2/ADR 0067: occupation-name-dimensionen Ssyk
/// utgick med VO-expansionen; Fas B2 2026-06-12: Klass 2 anställningsform/omfattning
/// tillkom; #311 PR-2b C1: Employer (org.nr); #551 PR-D: Remote (distans)).
/// Record-typer (t.ex. <c>ListJobAdsQuery</c>) matchar shape automatiskt via
/// primary-ctor-properties.</para>
///
/// <para><b>Auth-invariant (security-auditor F6 P4a Medium-3 2026-05-20):</b>
/// Endpoints som exponerar messages med denna markör <b>MÅSTE</b> ha
/// <c>.RequireAuthorization()</c> på endpoint-/route-nivå. Behavior kör no-op
/// vid anonym request (<c>ICurrentUser.UserId == null</c>) — det är defense-in-
/// depth, inte primär auth. Att markera en <c>[AllowAnonymous]</c>-query med
/// <c>ICapturesRecentSearch</c> är **felaktig** användning: capturen tystnar
/// men gör att framtida läsare antar att opt-in betyder "capture sker". Om en
/// ny query behöver auto-capture på anonyma flöden krävs separat
/// behavior-mekanik + ADR-amend.</para>
/// </summary>
public interface ICapturesRecentSearch
{
    string? Q { get; }
    IReadOnlyList<string>? OccupationGroup { get; }
    IReadOnlyList<string>? Municipality { get; }
    IReadOnlyList<string>? Region { get; }
    IReadOnlyList<string>? EmploymentType { get; }
    IReadOnlyList<string>? WorktimeExtent { get; }

    // #311 PR-2b C1 (ADR 0087 D6) — arbetsgivar-dimensionen (org.nr). PR-2 höll den MEDVETET
    // UTANFÖR detta interface (CONTAINED-scope: employer trådades bara in i live-sök-filtret, ej
    // sök-identiteten) → ListJobAdsQuery.Employer fångades aldrig. Denna PR (2b C1) lägger till den
    // så record-shapen matchar → en committad ?employer=-sökning fångas till RecentJobSearch.
    IReadOnlyList<string>? Employer { get; }

    // #551 PR-D — distans/remote-dimensionen (bool). PR-B höll den MEDVETET UTANFÖR detta interface
    // (CONTAINED-scope: remote trådades bara in i live-sök-filtret + facet-counts, ej sök-identiteten)
    // → ListJobAdsQuery.Remote fångades aldrig. Denna PR (PR-D) lägger till den så record-shapen
    // matchar → en committad ?remote=-sökning fångas till RecentJobSearch (parity Employer PR-2b C1).
    bool Remote { get; }

    JobAdSortBy SortBy { get; }

    /// <summary>
    /// Commit-intent-gate (Fas E2j, ADR 0060 amendment 2026-06-12). Behaviorn
    /// fångar ENDAST när detta är <c>true</c>. FE sätter det vid avsiktlig
    /// commit (Enter/Sök/förslags-val/toolbar); live-förhandsvisning per ord
    /// (<c>router.replace</c>) utelämnar det → no-op.
    ///
    /// <para>Bakgrund: E2i:s live-sök rev Beslut 3:s implicita premiss "en
    /// query = en intention" — varje committat ord triggade en RSC-render →
    /// list-query → capture, vilket fyllde cap=20 med mellanstegsspam som
    /// evictade äkta committade sökningar (data-minimerings-regression,
    /// GDPR Art. 5(1)(c)). Backend kan inte skilja <c>router.replace</c> från
    /// <c>router.push</c> — intentet måste bäras explicit. Detta är INTE den
    /// avvisade Variant B (separat command): flaggan rider på list-queryn som
    /// ändå körs (noll extra round-trip, ingen ny race, ingen trust-flytt
    /// utöver klientens egen historik). Se ADR 0060 amendment 2026-06-12.</para>
    /// </summary>
    bool Commit { get; }
}
