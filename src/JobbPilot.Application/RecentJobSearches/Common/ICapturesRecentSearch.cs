using JobbPilot.Domain.JobAds;

namespace JobbPilot.Application.RecentJobSearches.Common;

/// <summary>
/// Opt-in markör (ADR 0060) — queries som ska auto-capture:as till
/// RecentJobSearches när authenticated user kör sökning. Behaviorn
/// <c>RecentJobSearchCaptureBehavior</c> kör no-op för meddelanden utan
/// markören (paritet med <c>IRequiresFieldEncryptionKey</c>-mönstret).
///
/// <para>Interface exponerar de fält som tillsammans definierar filter-identitet
/// (Q, OccupationGroup, Municipality, Region, EmploymentType, WorktimeExtent,
/// SortBy — Fas C2/ADR 0067: occupation-name-dimensionen Ssyk utgick med VO-
/// expansionen; Fas B2 2026-06-12: Klass 2 anställningsform/omfattning tillkom).
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
