using Jobbliggaren.Application.CompanyWatches.Queries;
using Jobbliggaren.Application.NotificationFeed.Queries.GetMyNotificationFeed;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.NotificationFeed.Sources;

/// <summary>
/// The followed-company source (#1666): <see cref="NewFollowedCompanyAdSet"/> under
/// <c>LastSeenFollowedAdsAt</c>, the definition Översikt's "nya annonser från bevakade företag" reads,
/// so the bell's number is that count bounded by its token. One row per AD, never per hit
/// (<see cref="NewFollowedCompanyAdSet.CollapseToAds"/>), and only ads the inclusion rule admits — an
/// "endast matchade" watch's ad below Good is no notification. Not consent-gated: follow hits are
/// produced for every active follow, and their consent governs e-mail only.
///
/// <para>
/// <b>Token (#1576):</b> the newest of ALL new hits, including those the grade filter excludes, so an
/// excluded older hit cannot hold the watermark down and re-show the same ads. Acknowledging the whole
/// counted set is the explicit "Markera alla som lästa" over a number the user has seen (ADR 0162); a visit to
/// <c>/foretag/bevakade/nya</c> still acknowledges only the window it showed.
/// </para>
///
/// <para>
/// <b>Window and horizon:</b> the list reads the newest <see cref="HitsPerRow"/> hits per shown row and
/// filters them after reading, so an ad whose included hits all lie beyond the window can be missing. When the window
/// is full, the hit just past it is the horizon, and the merge shows no read row at or before it.
/// </para>
///
/// <para>
/// <b>Unread ads beyond the window (#2130):</b> on the grade path an "endast matchade" watch's excluded
/// hits can fill the window while older included ads are still unread and counted. The source then lists
/// the newest of those from the counted hits, which are already graded, so the number never counts rows
/// the panel cannot show. The common path needs no such step: every hit is included there.
/// </para>
/// </summary>
internal sealed class FollowedCompanyAdFeedSource : INotificationFeedSource
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

        var scope = await NewFollowedCompanyAdSet.LoadScopeAsync(db, userId, cancellationToken);
        if (scope is null)
            return NotificationSourceRead.Nothing(Kind);

        var token = await NewFollowedCompanyAdSet.NewHits(db, userId, scope.LastSeen)
            .MaxAsync(h => (DateTimeOffset?)h.CreatedAt, cancellationToken);

        var countInput = token is { } through
            ? await NewFollowedCompanyAdSet.LoadCountInputAsync(
                db, userId, scope, upperBound: through, maxGradeHits: maxCountedHits, cancellationToken)
            : NewFollowedCompanyAdSet.CountInput.Common(0);

        var windowHits = context.MaxItems * HitsPerRow;
        List<WindowRow> window = [];
        DateTimeOffset? horizon = null;
        if ((token ?? scope.LastSeen) is { } listBound)
        {
            window = await (
                    from h in NewFollowedCompanyAdSet.NewHits(db, userId, lastSeen: null)
                    where h.CreatedAt <= listBound
                    join j in db.JobAds.AsNoTracking() on h.JobAdId equals j.Id
                    orderby h.CreatedAt descending
                    select new WindowRow(h.JobAdId, h.CompanyWatchId, h.CreatedAt, j.Title, j.Company.Name))
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

        // An ad's moment is its newest INCLUDED hit, and it is unread when that hit is newer than the
        // watermark: an ad whose only new hit is excluded is no new notification.
        var items = window
            .Where(r => NewFollowedCompanyAdSet.Includes(
                new NewFollowedCompanyAdSet.Hit(r.JobAdId, r.CompanyWatchId, r.CreatedAt),
                scope.GradeWatchIds,
                resolution.Matching))
            .GroupBy(r => r.JobAdId)
            .Select(g => g.OrderByDescending(r => r.CreatedAt).First())
            .Select(r => new NotificationFeedItemDto(
                Kind,
                r.JobAdId.Value,
                r.Title,
                r.CompanyName,
                r.CreatedAt,
                IsUnread: scope.LastSeen is not { } seen || r.CreatedAt > seen,
                Grade: null))
            .ToList();

        // Grade path only (#2130): the counted hits ARE the unread hits up to the token, and `resolution`
        // graded every one of them, so the newest included unread ads the window never reached can be named
        // without a second grading call. Their display columns come from one more read of the same hits-to-ads
        // join the window uses, bounded by time; no id list goes to the database (Contains over the strongly
        // typed id is a known translation trap). The common path needs none of this: every hit is included.
        if (countInput.Exact is null && unreadCount is { } counted && token is { } upTo)
        {
            var shownUnread = items.Where(i => i.IsUnread).Select(i => i.JobAdId).ToHashSet();
            if (shownUnread.Count < Math.Min(counted, context.MaxItems))
            {
                var missing = NewestIncludedUnreadAds(countInput, scope.GradeWatchIds, resolution.Matching, context.MaxItems)
                    .Where(x => !shownUnread.Contains(x.AdId.Value))
                    .ToList();
                if (missing.Count > 0)
                {
                    var oldestWanted = missing.Min(x => x.At);
                    var rows = await (
                            from h in NewFollowedCompanyAdSet.NewHits(db, userId, scope.LastSeen)
                            where h.CreatedAt >= oldestWanted && h.CreatedAt <= upTo
                            join j in db.JobAds.AsNoTracking() on h.JobAdId equals j.Id
                            orderby h.CreatedAt descending
                            select new WindowRow(h.JobAdId, h.CompanyWatchId, h.CreatedAt, j.Title, j.Company.Name))
                        .ToListAsync(cancellationToken);
                    var display = rows.GroupBy(r => r.JobAdId).ToDictionary(g => g.Key, g => g.First());

                    items =
                    [
                        .. items,
                        .. missing
                            .Where(x => display.ContainsKey(x.AdId))
                            .Select(x => new NotificationFeedItemDto(
                                Kind, x.AdId.Value, display[x.AdId].Title, display[x.AdId].CompanyName, x.At,
                                IsUnread: true, Grade: null)),
                    ];
                    items = [.. items.OrderByDescending(i => i.OccurredAt)];
                }
            }
        }

        return new NotificationSourceRead(Kind, unreadCount, token, items, horizon);
    }

    /// <summary>
    /// The newest <paramref name="maxItems"/> ads the inclusion rule admits among the counted (unread) hits,
    /// each at its newest INCLUDED hit, the same moment the window assigns it. Pure: no database access.
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

    /// <summary>One hit joined to its ad's display columns. Named because an anonymous type cannot cross the projection.</summary>
    private sealed record WindowRow(
        JobAdId JobAdId, CompanyWatchId CompanyWatchId, DateTimeOffset CreatedAt, string Title, string CompanyName);
}
