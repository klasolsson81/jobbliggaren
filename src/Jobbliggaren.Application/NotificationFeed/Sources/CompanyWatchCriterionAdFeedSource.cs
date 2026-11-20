using Jobbliggaren.Application.CompanyWatches.Queries;
using Jobbliggaren.Application.NotificationFeed.Queries.GetMyNotificationFeed;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.NotificationFeed.Sources;

/// <summary>
/// The Branschbevakning source (#1663 PR 3, ADR 0175 D8): <see cref="NewCriterionAdSet.NewHits"/>, the one definition
/// that Översikt's "N nya" per criterion and each criterion's page of new ads already read, so the bell's number is that
/// set bounded by its token, less the hits read in the bell and the ads a higher source holds. One row per AD: several
/// criteria can reach the same ad, and the ad is the notification. Nothing is graded, so the count is an exact SQL
/// <c>COUNT(DISTINCT)</c> and there is no load bound. Not consent-gated: the hits are produced under Art. 6(1)(b) for
/// every criterion (ADR 0175 D4). It is read only while <see cref="NotificationFeedGate.CriterionAds"/> holds, the user's
/// own switch for this type.
///
/// <para>
/// <b>Its rows are the new set and nothing else (ADR 0175 D6).</b> There is no single watermark: each criterion has its
/// own (<c>NewAdsSeenThrough</c>, after <c>CriteriaChangedAt</c>), and <see cref="NewCriterionAdSet.NewHits"/> applies
/// it. So every row this source lists is new, unread means not read in the bell, and the list is bounded by the token
/// alone. A hit acknowledged on its criterion's page of new ads leaves the bell rather than staying as a read row.
/// </para>
///
/// <para>
/// <b>Below both other sources (ADR 0173 D7, ADR 0175 D8):</b> an ad the match source holds
/// (<see cref="MatchFeedSource.HeldAdIds"/>) or the follow source holds (<see cref="FollowedCompanyAdFeedSource.HeldAdIds"/>)
/// is their notification. The count, the window and the fallback leave its hits out in SQL, each exclusion its own
/// <c>NOT EXISTS</c> with its gate inside the statement; the token does not. Whether each gate holds is read in the first
/// statement, so with a gate off no statement names that source's table.
/// </para>
///
/// <para>
/// <b>Window and horizon:</b> the list reads the newest <see cref="HitsPerRow"/> hits per shown row, read ones included
/// and dismissed and held ones not, and collapses them per ad, so an ad whose hits all lie beyond the window can be
/// missing. When the window is full, the hit just past it is the horizon, and the merge shows no READ row at or before
/// it. When the window yields fewer unread ads than the count promises, one grouped statement names the newest unread
/// ads, so the base case stays at four statements.
/// </para>
/// </summary>
internal sealed class CompanyWatchCriterionAdFeedSource : INotificationFeedSource, INotificationFeedReadMarker
{
    /// <summary>Hits read for the list per row it can show, so collapsing several criteria's hits still fills the panel.</summary>
    internal const int HitsPerRow = 3;

    public NotificationFeedSourceKind Kind => NotificationFeedSourceKind.CompanyWatchCriterionAd;

    public async Task<NotificationSourceRead> ReadAsync(
        NotificationFeedReadContext context, CancellationToken cancellationToken)
    {
        var db = context.Db;
        var userId = context.UserId;

        // The user's switch for this type, whether each higher source's gate holds, and whether the user has a
        // Branschbevakning at all, in one statement: a user with the type off, or with nothing to watch, costs no further
        // read (Art. 5(1)(c)). The criterion check is an existence check only; what is new stays NewHits' to decide.
        var matchGate = NotificationFeedGate.Matches.Apply(db.JobSeekers);
        var followGate = NotificationFeedGate.FollowedAds.Apply(db.JobSeekers);
        var criteria = db.CompanyWatchCriteria.AsNoTracking();
        var seeker = await NotificationFeedGate.CriterionAds.Apply(db.JobSeekers)
            .Where(js => js.UserId == userId)
            .Select(js => new
            {
                MatchesReachBell = matchGate.Any(m => m.UserId == js.UserId),
                FollowsReachBell = followGate.Any(f => f.UserId == js.UserId),
                HasCriterion = criteria.Any(c => c.UserId == js.UserId),
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (seeker is not { HasCriterion: true })
            return NotificationSourceRead.Nothing(Kind);

        var heldByMatch = seeker.MatchesReachBell ? MatchFeedSource.HeldAdIds(db, userId) : null;
        var heldByFollow = seeker.FollowsReachBell ? FollowedCompanyAdFeedSource.HeldAdIds(db, userId) : null;

        // Token first, over every new hit, read or held alike: a hit committed between these statements is neither
        // counted nor listed, so it stays unread for the next poll. No token means the new set is empty. The newest hit
        // is read in order rather than by MAX: PostgreSQL's min/max rewrite does not cross a join, so MAX reads every hit.
        var token = await NewCriterionAdSet.NewHits(db, userId)
            .OrderByDescending(h => h.CreatedAt)
            .Select(h => (DateTimeOffset?)h.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (token is not { } through)
            return NotificationSourceRead.Nothing(Kind);

        var unreadCount = await NotHeld(NewCriterionAdSet.NewHits(db, userId), heldByMatch, heldByFollow)
            .Where(h => h.CreatedAt <= through && !h.ReadInNotificationFeed)
            .Select(h => h.JobAdId)
            .Distinct()
            .CountAsync(cancellationToken);

        var windowHits = context.MaxItems * HitsPerRow;
        var window = await (
                from h in NotHeld(NewCriterionAdSet.NewHits(db, userId), heldByMatch, heldByFollow)
                where h.CreatedAt <= through && !h.DismissedFromNotificationFeed
                join j in db.JobAds.AsNoTracking() on h.JobAdId equals j.Id
                orderby h.CreatedAt descending
                select new WindowRow(h.JobAdId, h.CreatedAt, j.Title, j.Company.Name, h.ReadInNotificationFeed))
            .Take(windowHits + 1)
            .ToListAsync(cancellationToken);

        DateTimeOffset? horizon = null;
        if (window.Count > windowHits)
        {
            horizon = window[windowHits].CreatedAt;
            window.RemoveRange(windowHits, window.Count - windowHits);
        }

        // An ad's moment is its newest hit in the window, and the ad is unread when any of its hits there is: the count's
        // own predicate, so a row and the count beside it cannot disagree.
        var items = window
            .GroupBy(r => r.JobAdId)
            .Select(g =>
            {
                var newest = g.OrderByDescending(r => r.CreatedAt).First();
                return new NotificationFeedItemDto(
                    Kind, newest.JobAdId.Value, newest.Title, newest.CompanyName, newest.CreatedAt,
                    IsUnread: g.Any(r => !r.Read), Grade: null);
            })
            .ToList();

        // The shortfall: the count names more unread ads than the window yielded (read hits crowd them out, or several
        // criteria reach the same ads). Name the newest of them from the counted hits.
        if (items.Count(i => i.IsUnread) < Math.Min(unreadCount, context.MaxItems))
        {
            var unreadAds = await (
                    from x in (
                        from h in NotHeld(NewCriterionAdSet.NewHits(db, userId), heldByMatch, heldByFollow)
                        where h.CreatedAt <= through && !h.ReadInNotificationFeed
                        group h by h.JobAdId into g
                        select new { JobAdId = g.Key, At = g.Max(h => h.CreatedAt) })
                    join j in db.JobAds.AsNoTracking() on x.JobAdId equals j.Id
                    orderby x.At descending, x.JobAdId
                    select new { x.JobAdId, x.At, j.Title, Company = j.Company.Name })
                .Take(context.MaxItems)
                .ToListAsync(cancellationToken);

            // One row per ad (ADR 0162 D2): the unread ad replaces the window's version of it (read, because its newest
            // hit is read while an older one is not), keeping the later of the two moments.
            var byAd = items.ToDictionary(i => i.JobAdId);
            foreach (var unread in unreadAds)
            {
                var row = new NotificationFeedItemDto(
                    Kind, unread.JobAdId.Value, unread.Title, unread.Company, unread.At, IsUnread: true, Grade: null);
                byAd[row.JobAdId] = byAd.TryGetValue(row.JobAdId, out var inWindow)
                    ? row with { OccurredAt = inWindow.OccurredAt > row.OccurredAt ? inWindow.OccurredAt : row.OccurredAt }
                    : row;
            }

            items = [.. byAd.Values.OrderByDescending(i => i.OccurredAt).ThenBy(i => i.JobAdId)];
        }

        return new NotificationSourceRead(Kind, unreadCount, token, items, horizon);
    }

    /// <summary>The hits of ads neither higher source holds; each exclusion is its own <c>NOT EXISTS</c>.</summary>
    private static IQueryable<CompanyWatchCriterionAdHit> NotHeld(
        IQueryable<CompanyWatchCriterionAdHit> hits, IQueryable<JobAdId>? heldByMatch, IQueryable<JobAdId>? heldByFollow)
    {
        if (heldByMatch is not null)
            hits = hits.Where(h => !heldByMatch.Any(id => id == h.JobAdId));
        if (heldByFollow is not null)
            hits = hits.Where(h => !heldByFollow.Any(id => id == h.JobAdId));
        return hits;
    }

    /// <summary>
    /// One hit joined to its ad's display columns. No criterion id, label or code: the wire carries none (DPIA 560 §D9,
    /// C-D9), so the read takes none (Art. 5(1)(c)).
    /// </summary>
    private sealed record WindowRow(JobAdId JobAdId, DateTimeOffset CreatedAt, string Title, string CompanyName, bool Read);

    // The writes on Branschbevakning hits: one set-based statement each, owner-scoped, and filtered on the state it writes,
    // so a repeated request writes nothing. Not through the aggregate: a tracked hit can be deleted between its load and
    // the unit of work's save (the nightly purge, a deleted criterion, Art. 17), which would fail the save and with it the
    // follow source's marks in the same request. No consent gates them (the hits are produced under Art. 6(1)(b)), and
    // the type's own switch does not either (see INotificationFeedReadMarker). They never touch a criterion, its
    // NewAdsSeenThrough or job_seekers, and none clears a flag.

    public Task MarkRowAsync(NotificationFeedMarkContext context, JobAdId jobAdId, CancellationToken cancellationToken)
    {
        var userId = context.UserId;
        // The bell's row is the ad, so every hit of the ad the owner has, through any criterion.
        return context.Db.CompanyWatchCriterionAdHits
            .Where(h => h.UserId == userId && h.JobAdId == jobAdId && !h.ReadInNotificationFeed)
            .ExecuteUpdateAsync(set => set.SetProperty(h => h.ReadInNotificationFeed, true), cancellationToken);
    }

    public Task DismissRowAsync(NotificationFeedMarkContext context, JobAdId jobAdId, CancellationToken cancellationToken)
    {
        var userId = context.UserId;
        // Filters on the dismissed flag, never the read flag: a hit read in the bell is dismissed too. Both flags in one
        // statement, as the CHECK requires.
        return context.Db.CompanyWatchCriterionAdHits
            .Where(h => h.UserId == userId && h.JobAdId == jobAdId && !h.DismissedFromNotificationFeed)
            .ExecuteUpdateAsync(set => set
                .SetProperty(h => h.DismissedFromNotificationFeed, true)
                .SetProperty(h => h.ReadInNotificationFeed, true), cancellationToken);
    }

    public Task MarkThroughAsync(
        NotificationFeedMarkContext context, DateTimeOffset through, CancellationToken cancellationToken)
    {
        var db = context.Db;
        var userId = context.UserId;
        // The counted set up to the token, held hits included: every new hit (each criterion's own watermark, through
        // NewHits, read and never moved), not yet read. Correlated on the hit's own key, so the definition is composed
        // and not restated. The bound and the flag stand inside it too: the planner does not carry the target's filter
        // across the key, and without them a call that marks nothing would compute all of NewHits.
        var counted = NewCriterionAdSet.NewHits(db, userId)
            .Where(n => n.CreatedAt <= through && !n.ReadInNotificationFeed);
        return db.CompanyWatchCriterionAdHits
            .Where(h => h.UserId == userId
                        && h.CreatedAt <= through
                        && !h.ReadInNotificationFeed
                        && counted.Any(n => n.Id == h.Id))
            .ExecuteUpdateAsync(set => set.SetProperty(h => h.ReadInNotificationFeed, true), cancellationToken);
    }

    public async Task MarkBacklogAsync(NotificationFeedMarkContext context, CancellationToken cancellationToken)
    {
        var db = context.Db;
        var userId = context.UserId;
        var stamp = await db.JobSeekers
            .Where(js => js.UserId == userId)
            .Select(js => new { js.Preferences.NotificationFeedCriterionAdsOffAt })
            .FirstOrDefaultAsync(cancellationToken);
        if (stamp?.NotificationFeedCriterionAdsOffAt is not { } offAt)
            return;

        await db.CompanyWatchCriterionAdHits
            .Where(h => h.UserId == userId && h.CreatedAt > offAt && !h.ReadInNotificationFeed)
            .ExecuteUpdateAsync(set => set.SetProperty(h => h.ReadInNotificationFeed, true), cancellationToken);
    }
}
