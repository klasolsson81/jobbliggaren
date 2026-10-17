using System.Linq.Expressions;
using Jobbliggaren.Application.CompanyWatches.Queries;
using Jobbliggaren.Application.NotificationFeed.Queries.GetMyNotificationFeed;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.NotificationFeed.Sources;

/// <summary>
/// The followed-company source (#1666): <see cref="NewFollowedCompanyAdSet"/> under
/// <c>LastSeenFollowedAdsAt</c>, the definition Översikt's "nya annonser från bevakade företag" reads,
/// so the bell's number is that count bounded by its token, less the hits the user has read in the bell (#2130) and
/// the ads the match source holds (#2158).
/// One row per AD, never per hit (<see cref="NewFollowedCompanyAdSet.CollapseToAds"/>), and only ads the inclusion
/// rule admits — an "endast matchade" watch's ad below Good is no notification. Not consent-gated: follow hits are
/// produced for every active follow, and their consent governs e-mail only. It is read only while
/// <see cref="NotificationFeedGate.FollowedAds"/> holds, the user's own switch for this type.
///
/// <para>
/// <b>Unread in the bell</b> = new since the watermark AND not read in the bell. A row is unread when ANY of its
/// included hits is. The <b>token (#1576)</b> is the newest of ALL new hits, flagged or not and including those the
/// grade filter excludes, so an excluded older hit cannot hold the watermark down and re-show the same ads, and a
/// hit read one by one can never strand the rest. The explicit "Markera alla som lästa" marks every new hit up to it,
/// held ones included; a visit to <c>/foretag/bevakade/nya</c> still acknowledges only the window it showed.
/// </para>
///
/// <para>
/// <b>Held ads (#2158, ADR 0173):</b> while match notifications reach the bell, an ad the match source holds
/// (<see cref="MatchFeedSource.HeldAdIds"/>) is its notification and not this one's: the count, the window and both
/// fallbacks leave its hits out in SQL, and the token does not. Whether the match gate holds is read in the first
/// statement, so with it off no statement here reads a match row; the held set checks the gate again inside every
/// statement that uses it.
/// </para>
///
/// <para>
/// <b>Window and horizon:</b> the list reads the newest <see cref="HitsPerRow"/> hits per shown row, read ones
/// included and held ones not, and filters them after reading, so an ad whose included hits all lie beyond the
/// window can be missing. When the window is full, the hit just past it is the horizon, and the merge shows no READ
/// row at or before it.
/// </para>
///
/// <para>
/// <b>The newest unread ads, on every path (#2130):</b> when the window yields fewer unread ads than the count
/// promises (an "endast matchade" watch's excluded hits fill it, hits read in the bell crowd the unread ones out, or
/// several watches reach the same ads), the source names the newest unread ads from the counted hits instead. On the
/// grade path they are already loaded and graded, so there is still one grading call per poll; on the common path
/// one grouped statement names them. The statement runs only on a shortfall, so the base case stays at five.
/// </para>
/// </summary>
internal sealed class FollowedCompanyAdFeedSource : INotificationFeedSource, INotificationFeedReadMarker
{
    /// <summary>
    /// The grade path's load bound. The common path is a SQL <c>COUNT(DISTINCT)</c> and needs none, but
    /// with an "endast matchade" watch every unacknowledged hit is loaded and its ad graded on every
    /// poll, and nothing limits how many companies a user follows. Past this many hits the source
    /// refuses to count (ADR 0120 clause 5) rather than show a floor. Derived, not measured: at the
    /// bell's 12 requests per minute it caps graded ids at 120 000 per user and minute, the order
    /// <c>CompanyWatchCriteriaList</c> already accepts. The bell's own constant, deliberately not tied to
    /// <c>ListNewFollowedCompanyAdsQueryHandler.MaxRows</c> or any other cap.
    /// </summary>
    internal const int MaxCountedHits = 10_000;

    /// <summary>Hits read for the list per row it can show, so collapsing and filtering still fill the panel.</summary>
    internal const int HitsPerRow = 3;

    public NotificationFeedSourceKind Kind => NotificationFeedSourceKind.FollowedCompanyAd;

    public Task<NotificationSourceRead> ReadAsync(
        NotificationFeedReadContext context, CancellationToken cancellationToken) =>
        ReadBoundedAsync(context, MaxCountedHits, cancellationToken);

    /// <summary>The read with an explicit count bound, so the refusal can be exercised with a small one.</summary>
    internal async Task<NotificationSourceRead> ReadBoundedAsync(
        NotificationFeedReadContext context, int maxCountedHits, CancellationToken cancellationToken)
    {
        var db = context.Db;
        var userId = context.UserId;

        // The user's switch for this type and the watermark, in one statement: a user who switched the type off costs
        // no further read (Art. 5(1)(c)). The scope below reads the watches with the watermark already in hand.
        var matchGate = NotificationFeedGate.Matches.Apply(db.JobSeekers);
        var seeker = await NotificationFeedGate.FollowedAds.Apply(db.JobSeekers)
            .Where(js => js.UserId == userId)
            .Select(js => new { js.LastSeenFollowedAdsAt, MatchesReachBell = matchGate.Any(m => m.UserId == js.UserId) })
            .FirstOrDefaultAsync(cancellationToken);
        if (seeker is null)
            return NotificationSourceRead.Nothing(Kind);

        var held = seeker.MatchesReachBell ? MatchFeedSource.HeldAdIds(db, userId) : null;

        var scope = await NewFollowedCompanyAdSet.LoadScopeAsync(db, userId, seeker.LastSeenFollowedAdsAt, cancellationToken);
        if (scope is null)
            return NotificationSourceRead.Nothing(Kind);

        var token = await NewFollowedCompanyAdSet.NewHits(db, userId, scope.LastSeen)
            .MaxAsync(h => (DateTimeOffset?)h.CreatedAt, cancellationToken);

        var countInput = token is { } through
            ? await NewFollowedCompanyAdSet.LoadCountInputAsync(
                db, userId, scope, upperBound: through, maxGradeHits: maxCountedHits,
                narrowing: CountedInTheBell(held), cancellationToken)
            : NewFollowedCompanyAdSet.CountInput.Common(0);

        var windowHits = context.MaxItems * HitsPerRow;
        List<WindowRow> window = [];
        DateTimeOffset? horizon = null;
        if ((token ?? scope.LastSeen) is { } listBound)
        {
            window = await (
                    from h in NotHeld(NewFollowedCompanyAdSet.NewHits(db, userId, lastSeen: null), held)
                    where h.CreatedAt <= listBound && !h.DismissedFromNotificationFeed
                    join j in db.JobAds.AsNoTracking() on h.JobAdId equals j.Id
                    orderby h.CreatedAt descending
                    select new WindowRow(
                        h.JobAdId, h.CompanyWatchId, h.CreatedAt, j.Title, j.Company.Name, h.ReadInNotificationFeed))
                .Take(windowHits + 1)
                .ToListAsync(cancellationToken);

            if (window.Count > windowHits)
            {
                horizon = window[windowHits].CreatedAt;
                window.RemoveRange(windowHits, window.Count - windowHits);
            }
        }

        var shownHits = window
            .Select(r => new NewFollowedCompanyAdSet.Hit(r.JobAdId, r.CompanyWatchId, r.CreatedAt))
            .ToList();

        // One grading call per request, over the counted and the displayed candidates together.
        var candidates = NewFollowedCompanyAdSet.GradeCandidates(countInput.GradeHits, scope.GradeWatchIds)
            .Concat(NewFollowedCompanyAdSet.GradeCandidates(shownHits, scope.GradeWatchIds))
            .Distinct()
            .ToList();
        var resolution = await NewFollowedCompanyAdSet.ResolveMatchingAsync(
            context.ProfileBuilder, context.PerUserSearch, candidates, cancellationToken);

        var unreadCount = NewFollowedCompanyAdSet.CountAds(countInput, scope.GradeWatchIds, resolution.Matching);

        // An ad's moment is its newest INCLUDED hit, and the ad is unread when ANY included hit of it in the window is
        // both newer than the watermark and unread in the bell: the count's own predicate, so a row and the count
        // beside it cannot disagree (I3). An ad whose only new hit is excluded is no new notification.
        var items = window
            .Where(r => NewFollowedCompanyAdSet.Includes(
                new NewFollowedCompanyAdSet.Hit(r.JobAdId, r.CompanyWatchId, r.CreatedAt),
                scope.GradeWatchIds,
                resolution.Matching))
            .GroupBy(r => r.JobAdId)
            .Select(g =>
            {
                var newest = g.OrderByDescending(r => r.CreatedAt).First();
                return new NotificationFeedItemDto(
                    Kind,
                    newest.JobAdId.Value,
                    newest.Title,
                    newest.CompanyName,
                    newest.CreatedAt,
                    IsUnread: g.Any(r => (scope.LastSeen is not { } seen || r.CreatedAt > seen) && !r.Read),
                    Grade: null);
            })
            .ToList();

        // The shortfall: the count names more unread ads than the window yielded. Name the newest of them from the
        // counted hits. `unreadCount` is exact and bounded by the token, as are these reads.
        if (unreadCount is { } counted && token is { } upTo
            && items.Count(i => i.IsUnread) < Math.Min(counted, context.MaxItems))
        {
            var unreadAds = await ReadUnreadBeyondWindowAsync(
                context, scope, countInput, resolution.Matching, held, upTo, cancellationToken);

            // One row per ad (ADR 0162 D2): the unread ad replaces the window's version of it (read, because its newest
            // hit is read while an older one is not), keeping the later of the two moments.
            var byAd = items.ToDictionary(i => i.JobAdId);
            foreach (var unread in unreadAds)
                byAd[unread.JobAdId] = byAd.TryGetValue(unread.JobAdId, out var inWindow)
                    ? unread with { OccurredAt = inWindow.OccurredAt > unread.OccurredAt ? inWindow.OccurredAt : unread.OccurredAt }
                    : unread;
            items = [.. byAd.Values.OrderByDescending(i => i.OccurredAt).ThenBy(i => i.JobAdId)];
        }

        return new NotificationSourceRead(Kind, unreadCount, token, items, horizon);
    }

    /// <summary>
    /// The newest unread ads (at most <see cref="NotificationFeedReadContext.MaxItems"/>) from the counted hits, as
    /// unread rows. Grade path: the counted hits are in memory and graded already, so the ads are chosen with no second
    /// grading call and their display columns come from one more read of the same hits-to-ads join, bounded by time
    /// (no id list goes to the database: <c>Contains</c> over the strongly typed id is a known translation trap).
    /// Common path: no hit is loaded, so one grouped statement names the newest unread ads and joins their display
    /// columns. Both inherit the Active gate from <see cref="NewFollowedCompanyAdSet.NewHits"/>.
    /// </summary>
    private async Task<List<NotificationFeedItemDto>> ReadUnreadBeyondWindowAsync(
        NotificationFeedReadContext context,
        NewFollowedCompanyAdSet.Scope scope,
        NewFollowedCompanyAdSet.CountInput countInput,
        IReadOnlySet<JobAdId> matching,
        IQueryable<JobAdId>? held,
        DateTimeOffset upTo,
        CancellationToken cancellationToken)
    {
        var db = context.Db;
        var userId = context.UserId;

        if (countInput.Exact is not null)
        {
            // Common path: with no "endast matchade" watch every hit is included, so the newest unread ads are a plain
            // grouping of the unread hits.
            var rows = await (
                    from x in (
                        from h in NotHeld(NewFollowedCompanyAdSet.NewHits(db, userId, scope.LastSeen), held)
                        where h.CreatedAt <= upTo && !h.ReadInNotificationFeed
                        group h by h.JobAdId into g
                        select new { JobAdId = g.Key, At = g.Max(h => h.CreatedAt) })
                    join j in db.JobAds.AsNoTracking() on x.JobAdId equals j.Id
                    orderby x.At descending, x.JobAdId
                    select new { x.JobAdId, x.At, j.Title, Company = j.Company.Name })
                .Take(context.MaxItems)
                .ToListAsync(cancellationToken);

            return
            [
                .. rows.Select(r => new NotificationFeedItemDto(
                    Kind, r.JobAdId.Value, r.Title, r.Company, r.At, IsUnread: true, Grade: null)),
            ];
        }

        var wanted = NewestIncludedUnreadAds(countInput, scope.GradeWatchIds, matching, context.MaxItems);
        if (wanted.Count == 0)
            return [];

        var oldestWanted = wanted.Min(x => x.At);
        var hits = await (
                from h in NotHeld(NewFollowedCompanyAdSet.NewHits(db, userId, scope.LastSeen), held)
                where h.CreatedAt >= oldestWanted && h.CreatedAt <= upTo && !h.ReadInNotificationFeed
                join j in db.JobAds.AsNoTracking() on h.JobAdId equals j.Id
                orderby h.CreatedAt descending
                select new { h.JobAdId, j.Title, Company = j.Company.Name })
            // The same hits the count loaded (unread, not held, past the watermark, up to the token), narrowed by time,
            // so they number at most the counted hits and every wanted ad is among them.
            .Take(countInput.GradeHits.Count)
            .ToListAsync(cancellationToken);
        var display = hits.GroupBy(r => r.JobAdId).ToDictionary(g => g.Key, g => g.First());

        return
        [
            .. wanted
                .Where(x => display.ContainsKey(x.AdId))
                .Select(x => new NotificationFeedItemDto(
                    Kind, x.AdId.Value, display[x.AdId].Title, display[x.AdId].Company, x.At, IsUnread: true, Grade: null)),
        ];
    }

    /// <summary>
    /// The newest <paramref name="maxItems"/> ads the inclusion rule admits among the counted (unread) hits,
    /// each at its newest included hit among them. Pure: no database access.
    /// </summary>
    private static List<(JobAdId AdId, DateTimeOffset At)> NewestIncludedUnreadAds(
        NewFollowedCompanyAdSet.CountInput countInput,
        IReadOnlySet<CompanyWatchId> gradeWatchIds,
        IReadOnlySet<JobAdId> matching,
        int maxItems) =>
        countInput.GradeHits
            .Where(h => NewFollowedCompanyAdSet.Includes(h, gradeWatchIds, matching))
            .GroupBy(h => h.JobAdId)
            .Select(g => (AdId: g.Key, At: g.Max(h => h.CreatedAt)))
            .OrderByDescending(x => x.At)
            .ThenBy(x => x.AdId.Value)
            .Take(maxItems)
            .ToList();

    /// <summary>The hits the bell counts: unread in the bell, and of an ad the match source does not hold.</summary>
    private static Expression<Func<FollowedCompanyAdHit, bool>> CountedInTheBell(IQueryable<JobAdId>? held)
    {
        if (held is null)
            return h => !h.ReadInNotificationFeed;
        return h => !h.ReadInNotificationFeed && !held.Any(id => id == h.JobAdId);
    }

    /// <summary>The hits of ads the match source does not hold; every hit when it holds none.</summary>
    private static IQueryable<FollowedCompanyAdHit> NotHeld(IQueryable<FollowedCompanyAdHit> hits, IQueryable<JobAdId>? held) =>
        held is null ? hits : hits.Where(h => !held.Any(id => id == h.JobAdId));

    /// <summary>One hit joined to its ad's display columns. Named because an anonymous type cannot cross the projection.</summary>
    private sealed record WindowRow(
        JobAdId JobAdId, CompanyWatchId CompanyWatchId, DateTimeOffset CreatedAt, string Title, string CompanyName,
        bool Read);

    // Writes on follow rows are not consent-gated: the rows are produced for every active follow, and the follow
    // consent governs e-mail only (ADR 0162 D6). Nor are they filtered by inclusion or the ad's lifecycle: a mark reaches
    // exactly the rows the watermark it replaces reached, and reads no ad.

    public async Task MarkRowAsync(NotificationFeedMarkContext context, JobAdId jobAdId, CancellationToken cancellationToken)
    {
        var userId = context.UserId;
        // The bell's row is the ad, so every hit of the ad the owner has, through any watch.
        var hits = await context.Db.FollowedCompanyAdHits
            .Where(h => h.UserId == userId && h.JobAdId == jobAdId && !h.ReadInNotificationFeed)
            .ToListAsync(cancellationToken);

        // The unit of work commits them. No hit, or a hit already read, is no row at all.
        foreach (var hit in hits)
            hit.MarkReadInNotificationFeed();
    }

    public async Task DismissRowAsync(NotificationFeedMarkContext context, JobAdId jobAdId, CancellationToken cancellationToken)
    {
        var userId = context.UserId;
        // Every hit of the ad the owner has that is not dismissed yet, read in the bell or not: filtering on the read
        // flag here would leave a hit the user already opened on screen. The unit of work commits them.
        var hits = await context.Db.FollowedCompanyAdHits
            .Where(h => h.UserId == userId && h.JobAdId == jobAdId && !h.DismissedFromNotificationFeed)
            .ToListAsync(cancellationToken);

        foreach (var hit in hits)
            hit.DismissFromNotificationFeed();
    }

    public async Task MarkThroughAsync(
        NotificationFeedMarkContext context, DateTimeOffset through, CancellationToken cancellationToken)
    {
        var db = context.Db;
        var userId = context.UserId;
        var seeker = await db.JobSeekers
            .Where(js => js.UserId == userId)
            .Select(js => new { js.LastSeenFollowedAdsAt })
            .FirstOrDefaultAsync(cancellationToken);
        if (seeker is null)
            return;

        var watermark = seeker.LastSeenFollowedAdsAt;
        await db.FollowedCompanyAdHits
            .Where(h => h.UserId == userId
                        && h.CreatedAt <= through
                        && (watermark == null || h.CreatedAt > watermark)
                        && !h.ReadInNotificationFeed)
            .ExecuteUpdateAsync(set => set.SetProperty(h => h.ReadInNotificationFeed, true), cancellationToken);
    }

    public async Task MarkBacklogAsync(NotificationFeedMarkContext context, CancellationToken cancellationToken)
    {
        var db = context.Db;
        var userId = context.UserId;
        var stamp = await db.JobSeekers
            .Where(js => js.UserId == userId)
            .Select(js => new { js.Preferences.NotificationFeedFollowedAdsOffAt })
            .FirstOrDefaultAsync(cancellationToken);
        if (stamp?.NotificationFeedFollowedAdsOffAt is not { } offAt)
            return;

        await db.FollowedCompanyAdHits
            .Where(h => h.UserId == userId && h.CreatedAt > offAt && !h.ReadInNotificationFeed)
            .ExecuteUpdateAsync(set => set.SetProperty(h => h.ReadInNotificationFeed, true), cancellationToken);
    }
}
