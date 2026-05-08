using System.Linq.Expressions;
using JobbPilot.Domain.Applications;

namespace JobbPilot.Application.Applications.Specifications;

/// <summary>
/// Predikat för att identifiera kandidat-aggregat för "stale"-detektering.
/// Tvådelat per arch-rapport (Plan B): SQL-filter snävar via Status (EF-översättbart,
/// utnyttjar partial-index <c>ix_applications_stale_detection</c>), client-side filter
/// avgör per-app-threshold via <c>LastStatusChangeAt + GhostedThresholdDays.Days &lt; now</c>.
///
/// Definition of stale (per Klas Fas 1, dokumenterat i ADR 0023):
/// transient-states där företaget förväntas svara — <c>Submitted</c> och
/// <c>Acknowledged</c>. Intervju-states (<c>InterviewScheduled</c>, <c>Interviewing</c>)
/// betraktas active oavsett kalendertid. Utökning till intervju-states spåras som
/// tech-debt och aktiveras vid första rapporterade fall.
///
/// DeletedAt-villkoret hanteras av <c>AppDbContext</c>:s globala query filter
/// per <c>ApplicationConfiguration</c>, så det upprepas inte här.
/// </summary>
public static class StaleApplicationSpecification
{
    /// <summary>
    /// SQL-snävande predikat: Status ∈ {Submitted, Acknowledged}.
    /// Kombineras med client-side <see cref="IsStaleNow"/> efter materialisering.
    /// </summary>
    public static Expression<Func<DomainApplication, bool>> CandidateStatusFilter() =>
        a => a.Status == ApplicationStatus.Submitted || a.Status == ApplicationStatus.Acknowledged;

    /// <summary>
    /// Client-side stale-check: per-app threshold-jämförelse. EF Core 10 / Npgsql 10
    /// översätter inte <c>DateTimeOffset.AddDays(int kolumn)</c> tillförlitligt — denna
    /// metod körs efter materialisering över det redan filtrerade kandidat-setet.
    /// </summary>
    public static bool IsStaleNow(DateTimeOffset lastStatusChangeAt, int ghostedThresholdDays, DateTimeOffset now) =>
        lastStatusChangeAt.AddDays(ghostedThresholdDays) < now;
}
