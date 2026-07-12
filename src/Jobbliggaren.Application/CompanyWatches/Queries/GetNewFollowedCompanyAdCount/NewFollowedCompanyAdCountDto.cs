namespace Jobbliggaren.Application.CompanyWatches.Queries.GetNewFollowedCompanyAdCount;

/// <summary>
/// Bevakning F2 (#801, RF-6=6B) — the count of new ads from employers the user follows, NEW since
/// the user last visited the follows surface (<c>FollowedCompanyAdHit.CreatedAt &gt;
/// JobSeeker.LastSeenFollowedAdsAt</c>, per-watch grade-filtered read-time via the shared ≥Good
/// SSOT). Drives the Översikt "nya annonser från bevakade företag"-row (sibling of the "Nya
/// matchningar"-row). <c>Count == 0</c> when there is no authenticated user, no JobSeeker, no active
/// follows, or nothing new since the watermark (all honest).
///
/// <para>
/// <b>D8 / GDPR (strongest posture):</b> a bare count — this DTO carries NO org.nr and NO company
/// name (Klas surface decision 2026-07-12: the Översikt row is a generic ledger row "från bevakade
/// företag", so no per-company breakdown is surfaced). The rail query therefore reads NO org.nr at
/// all (the watch join is on the opaque <c>CompanyWatchId</c>), so the surfacing-guard's DTO
/// partition is not even tripped ("rätt säkert läge", DPIA Part E §E8 point 4). The grade is a
/// read-time predicate only, never surfaced or persisted (Goodhart, C-E2).
/// </para>
/// </summary>
public sealed record NewFollowedCompanyAdCountDto(int Count)
{
    public static readonly NewFollowedCompanyAdCountDto Zero = new(0);
}
