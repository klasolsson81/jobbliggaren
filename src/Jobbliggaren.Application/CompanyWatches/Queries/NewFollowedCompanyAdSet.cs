using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.CompanyWatches.Queries;

/// <summary>
/// #1576 — the single definition of "this user's NEW ads from followed companies", shared by the two
/// entry points that must never disagree: <c>GetNewFollowedCompanyAdCountQueryHandler</c> (the
/// /oversikt number) and <c>ListNewFollowedCompanyAdsQueryHandler</c> (the destination that number
/// links to). Single-sourced for the same reason as <c>CompanyWatchFollowExecutor</c> (ADR 0087 D3
/// FORK B1): a count and its set that run two predicates cannot be made to agree, and a number that
/// promises more than its destination delivers is the defect #1576 exists to close (ADR 0120 — a
/// rendered count is true or it is absent).
///
/// <para>
/// <b>What must not drift is not the SQL where-clause — it is the whole definition</b>: the
/// watermark, the active-watch set, the per-watch OnlyMatched fork, and the profile-less-is-inert
/// rule. A duplicated grade fork would let the count exclude a filtered watch's ad that the list
/// still shows.
/// </para>
///
/// <para>
/// <b>D8 SEAL, RESTATED because it must not rot:</b> the <c>JobAds</c> join reads <c>j.Status</c> and
/// PROJECTS NOTHING from JobAds (ADR 0087 D8). The seal is the reason the join contributes a
/// <c>where</c>, never a <c>select</c>. The hit-to-watch join is on the opaque
/// <see cref="CompanyWatchId"/>, so this set reads no org.nr at all.
/// </para>
/// </summary>
internal static class NewFollowedCompanyAdSet
{
    /// <summary>One new hit. Hit columns ONLY — the D8 seal above is why no ad column is here.</summary>
    internal sealed record Hit(JobAdId JobAdId, CompanyWatchId CompanyWatchId, DateTimeOffset CreatedAt);

    /// <summary>
    /// The per-user reading context. A <c>null</c> return from <see cref="LoadScopeAsync"/> means "no
    /// active follows" — both consumers answer empty rather than querying hits.
    /// </summary>
    internal sealed record Scope(DateTimeOffset? LastSeen, IReadOnlySet<CompanyWatchId> GradeWatchIds);

    /// <summary>
    /// The read-time grade answer. <c>Assessed == false</c> means the user stated no occupation, so
    /// the OnlyMatched filter is INERT and <see cref="Matching"/> admits every candidate. A consumer
    /// that RENDERS matching (rather than only counting it) must read <c>Assessed</c> and say "not
    /// assessed" instead of claiming every row matches.
    /// </summary>
    internal sealed record GradeResolution(IReadOnlySet<JobAdId> Matching, bool Assessed);

    /// <summary>
    /// Watermark + active watches. <c>LastSeen == null</c> (never acknowledged) means every hit is
    /// new. The <c>Filter</c> is a property-converted opaque jsonb blob, so OnlyMatched can only be
    /// inspected in memory (never a server-side predicate) — parity DigestDispatchJob.
    /// </summary>
    public static async Task<Scope?> LoadScopeAsync(
        IAppDbContext db, Guid userId, CancellationToken cancellationToken)
    {
        var lastSeen = await db.JobSeekers
            .Where(js => js.UserId == userId)
            .Select(js => js.LastSeenFollowedAdsAt)
            .FirstOrDefaultAsync(cancellationToken);

        var activeWatches = await db.CompanyWatches
            .AsNoTracking()
            .Where(w => w.UserId == userId)
            .Select(w => new { w.Id, w.Filter })
            .ToListAsync(cancellationToken);

        if (activeWatches.Count == 0)
            return null;

        var gradeWatchIds = activeWatches
            .Where(w => w.Filter is { OnlyMatched: true })
            .Select(w => w.Id)
            .ToHashSet();

        return new Scope(lastSeen, gradeWatchIds);
    }

    /// <summary>
    /// New hits since the watermark, restricted to the user's ACTIVE watches by an equijoin on the
    /// (opaque) CompanyWatchId — NOT an org.nr read (D8). The global soft-delete filters on both
    /// sides exclude deleted hits and unfollowed watches. Joining (not an id-set Contains) also
    /// sidesteps the strongly-typed-VO Contains translation trap. STATUS-AGNOSTIC with respect to
    /// NOTIFICATION delivery (no NotificationStatus / SeenAt predicate) — that is a different axis
    /// from the ad's LIFECYCLE, gated here.
    ///
    /// <para>
    /// <b>LIFECYCLE (#864)</b> — the join to JobAds with <c>Status == Active</c> is why this rail is
    /// honest. Before it, the count joined JobAds NOT AT ALL: it counted hit rows, so an ARCHIVED ad
    /// was counted while its destination was gated. ALLOW-list (<c>== Active</c>), never
    /// <c>!= Archived</c>: a deny-list admits every status added later, and Erased (#842) is an
    /// Art. 17 tombstone that <c>!= Archived</c> would count.
    /// </para>
    ///
    /// <para>
    /// Query syntax plus <c>j.Status == JobAdStatus.Active</c> is the ONE translation form this repo
    /// has proven against Npgsql (the match badge writes it identically; the comparison crosses a
    /// SmartEnum HasConversion, and a form that dies at runtime is invisible under InMemory).
    /// Testcontainers (FollowedCompanyAdRailTests) is the oracle that proves this one translates AND
    /// that the count and the list return the same set.
    /// </para>
    /// </summary>
    public static IQueryable<Hit> NewHits(IAppDbContext db, Guid userId, DateTimeOffset? lastSeen) =>
        from h in db.FollowedCompanyAdHits
        where h.UserId == userId && (lastSeen == null || h.CreatedAt > lastSeen)
        join w in db.CompanyWatches on h.CompanyWatchId equals w.Id
        where w.UserId == userId
        join j in db.JobAds.AsNoTracking() on h.JobAdId equals j.Id
        where j.Status == JobAdStatus.Active
        select new Hit(h.JobAdId, h.CompanyWatchId, h.CreatedAt);

    /// <summary>
    /// Read-time at-least-Good membership via the shared SSOT. Api-side, so
    /// <c>BuildFullForSortAsync</c> (ICurrentUser-scoped), NOT the Worker's
    /// <c>BuildFullForUserIdAsync</c>. Branch on assessability BEFORE the call —
    /// <c>FilterToMatchingAsync</c> fail-fasts on an empty-SSYK profile.
    /// </summary>
    public static async Task<GradeResolution> ResolveMatchingAsync(
        IMatchProfileBuilder profileBuilder,
        IPerUserJobAdSearchQuery perUserSearch,
        IReadOnlyCollection<JobAdId> idsToGrade,
        CancellationToken cancellationToken)
    {
        if (idsToGrade.Count == 0)
            return new GradeResolution(new HashSet<JobAdId>(), Assessed: true);

        var profile = await profileBuilder.BuildFullForSortAsync(cancellationToken);
        if (profile.Fast.SsykGroupConceptIds.Count == 0)
            return new GradeResolution(idsToGrade.ToHashSet(), Assessed: false);

        var matching = await perUserSearch.FilterToMatchingAsync(profile, idsToGrade, cancellationToken);
        return new GradeResolution(matching, Assessed: true);
    }

    /// <summary>
    /// The inclusion rule, single-sourced: a hit under a plain watch always belongs to the set; a hit
    /// under an "endast matchade" watch belongs only when its ad grades at least Good. This is the
    /// one predicate the number and its destination share.
    /// </summary>
    public static bool Includes(
        Hit hit, IReadOnlySet<CompanyWatchId> gradeWatchIds, IReadOnlySet<JobAdId> matching) =>
        !gradeWatchIds.Contains(hit.CompanyWatchId) || matching.Contains(hit.JobAdId);

    /// <summary>
    /// Collapses hit rows to the ADS the user is told about. <b>The rule is OR over an ad's hits: an
    /// ad belongs to the set iff AT LEAST ONE of its hit rows passes <see cref="Includes"/>.</b> A
    /// plain <c>Distinct()</c> over rows is wrong — an ad reached through BOTH a plain watch and an
    /// "endast matchade" watch, grading below Good, has one failing hit and one passing hit, and it
    /// belongs.
    ///
    /// <para>
    /// <b>Why ads and not rows (senior-cto-advisor 2026-08-31, #1576).</b> Storage is deliberately
    /// hit-granular: <c>UNIQUE (user_id, job_ad_id, company_watch_id)</c> exists so "the same ad
    /// matched via TWO of a user's follows is two honest, independently-dispatched rows"
    /// (<c>FollowedCompanyAdHitConfiguration</c>) — an intent keyed on notification DISPATCH, which
    /// says nothing about counting. The user-facing unit was already decided the other way in
    /// delivered code: <c>MarkFollowedCompanyAdSeenCommandHandler</c> stamps both rows because "the
    /// user saw the AD". The count was the one place still reading the bookkeeping unit as the user
    /// unit, so it said "4 nya annonser" over 3 ads. The unique key is untouched.
    /// </para>
    /// </summary>
    public static IReadOnlySet<JobAdId> CollapseToAds(
        IEnumerable<Hit> hits,
        IReadOnlySet<CompanyWatchId> gradeWatchIds,
        IReadOnlySet<JobAdId> matching) =>
        hits.Where(h => Includes(h, gradeWatchIds, matching))
            .Select(h => h.JobAdId)
            .ToHashSet();
}
