using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Matching.Queries;
using Jobbliggaren.Application.NotificationFeed.Queries.GetMyNotificationFeed;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Matching;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.NotificationFeed.Sources;

/// <summary>
/// The background-match source (#1666): <see cref="NewMatchSet"/> under <c>LastSeenMatchesAt</c>, the
/// same definition as the uncapped new-match count (ADR 0080 Beslut 6), so the bell's number is that
/// count bounded by its token, less the rows the user has read in the bell (#2130).
///
/// <para>
/// <b>Who gets this source (ADR 0162 D6, security V1):</b> match rows are produced under the background-match
/// consent, and a withdrawal leaves them in place. The bell is a notification surface, so it reads this source only
/// while <see cref="NotificationFeedGate.Matches"/> holds: the consent AND the user's own switch for matches. Without
/// either the source says nothing and reads no row. The gate is the source's, not the set's: <c>/matchningar</c> and
/// the count keep reading the rows (security-auditor, #1666 form round).
/// </para>
///
/// <para>
/// <b>Unread in the bell</b> = new since the watermark AND not read in the bell. The <b>token</b> is still the newest
/// row of the watermark-new set, flagged or not: it stands for "everything up to here", so a row read one by one can
/// never strand the rest above a token computed over unread rows only. The count is bounded by it; "Markera alla"
/// marks the counted set up to it. Nothing here moves a watermark.
/// </para>
///
/// <para>
/// <b>Newest unread rows, on every path (#2130):</b> the list is the source's newest rows (a row taken out of the bell
/// with the X is not one, #2151), and when read rows crowd
/// unread ones out of that window a second statement names the newest unread rows. It runs only then, so the base
/// case stays at four statements. Each ad is listed once.
/// </para>
///
/// <para>
/// No horizon: the list is the source's exact newest rows that have not been taken out of the bell with the X (#2151)
/// plus its newest unread rows, so no newer read row the user has not dismissed can be missing while an older one is
/// shown.
/// </para>
/// </summary>
internal sealed class MatchFeedSource : INotificationFeedSource, INotificationFeedReadMarker
{
    public NotificationFeedSourceKind Kind => NotificationFeedSourceKind.Match;

    public async Task<NotificationSourceRead> ReadAsync(
        NotificationFeedReadContext context, CancellationToken cancellationToken)
    {
        var db = context.Db;
        var userId = context.UserId;

        var seeker = await NotificationFeedGate.Matches.Apply(db.JobSeekers)
            .Where(js => js.UserId == userId)
            .Select(js => new { js.LastSeenMatchesAt })
            .FirstOrDefaultAsync(cancellationToken);
        if (seeker is null)
            return NotificationSourceRead.Nothing(Kind);

        var watermark = seeker.LastSeenMatchesAt;
        var watermarkNew = NewMatchSet.Matches(db, userId, watermark);

        // Token first, then everything bounded by it: a match committed between these statements is
        // neither counted nor acknowledged, so it stays unread for the next poll. The token is over the
        // watermark-new set WITHOUT the read flag (see the class doc).
        var token = await watermarkNew.MaxAsync(m => (DateTimeOffset?)m.CreatedAt, cancellationToken);
        var unreadCount = token is { } through
            ? await watermarkNew.Where(m => m.CreatedAt <= through && !m.ReadInNotificationFeed).CountAsync(cancellationToken)
            : 0;

        // Nothing unread and never acknowledged means no match exists at all.
        if ((token ?? watermark) is not { } listBound)
            return new NotificationSourceRead(Kind, unreadCount, token, [], null);

        var rows = await (
                from m in NewMatchSet.Matches(db, userId, since: null)
                where m.CreatedAt <= listBound && !m.DismissedFromNotificationFeed
                join j in db.JobAds.AsNoTracking() on m.JobAdId equals j.Id
                orderby m.CreatedAt descending, m.Id
                select new MatchRow(m.JobAdId, j.Title, j.Company.Name, m.Grade, m.CreatedAt, m.ReadInNotificationFeed))
            .Take(context.MaxItems)
            .ToListAsync(cancellationToken);

        var items = rows.Select(r => ToItem(r, watermark)).ToList();

        // Read rows can crowd unread ones out of the newest-first window: 20 newer rows read in the bell, and an
        // older unread one the count still counts. Then the newest unread rows are named by their own statement.
        // `unreadCount` is exact and bounded by the token, as the list is, so the shortfall is real.
        if (token is { } upTo && items.Count(i => i.IsUnread) < Math.Min(unreadCount, context.MaxItems))
        {
            var unreadRows = await (
                    from m in watermarkNew
                    where m.CreatedAt <= upTo && !m.ReadInNotificationFeed
                    join j in db.JobAds.AsNoTracking() on m.JobAdId equals j.Id
                    orderby m.CreatedAt descending, m.Id
                    select new MatchRow(m.JobAdId, j.Title, j.Company.Name, m.Grade, m.CreatedAt, m.ReadInNotificationFeed))
                .Take(context.MaxItems)
                .ToListAsync(cancellationToken);

            // One row per ad (ADR 0162 D2): this statement's version of an ad replaces the window's, and agrees with it.
            var named = unreadRows.Select(r => r.JobAdId.Value).ToHashSet();
            items =
            [
                .. items.Where(i => !named.Contains(i.JobAdId)),
                .. unreadRows.Select(r => ToItem(r, watermark)),
            ];
        }

        return new NotificationSourceRead(Kind, unreadCount, token, items, Horizon: null);
    }

    /// <summary>
    /// The ads this source holds (#2158, ADR 0173): every ad the user has a match row for, read, dismissed or behind the
    /// watermark alike, while <see cref="NotificationFeedGate.Matches"/> holds. A later source leaves these ads out, so an ad
    /// is one notification. The gate is inside the query, so a statement that composes it reads a match row only while the
    /// gate holds in that statement.
    /// </summary>
    internal static IQueryable<JobAdId> HeldAdIds(IAppDbContext db, Guid userId)
    {
        var gated = NotificationFeedGate.Matches.Apply(db.JobSeekers);
        return NewMatchSet.Matches(db, userId, since: null)
            .Where(m => gated.Any(js => js.UserId == m.UserId))
            .Select(m => m.JobAdId);
    }

    /// <summary>One match joined to its ad's display columns. Named because an anonymous type cannot cross the helper.</summary>
    private sealed record MatchRow(
        JobAdId JobAdId, string Title, string Company, NotifiableMatchGrade Grade, DateTimeOffset CreatedAt, bool Read);

    private NotificationFeedItemDto ToItem(MatchRow row, DateTimeOffset? watermark) => new(
        Kind,
        row.JobAdId.Value,
        row.Title,
        row.Company,
        row.CreatedAt,
        IsUnread: (watermark is not { } seen || row.CreatedAt > seen) && !row.Read,
        row.Grade);

    // The gate for every write on a match row: the background-match consent, in the same statement as the write.
    // Never the user's switch for the type (see INotificationFeedReadMarker).

    public async Task MarkRowAsync(NotificationFeedMarkContext context, JobAdId jobAdId, CancellationToken cancellationToken)
    {
        var db = context.Db;
        var userId = context.UserId;
        await db.UserJobAdMatches
            .Where(m => m.UserId == userId && m.JobAdId == jobAdId && !m.ReadInNotificationFeed
                        && db.JobSeekers.Where(NotificationConsent.BackgroundMatch.Criteria).Any(js => js.UserId == m.UserId))
            .ExecuteUpdateAsync(set => set.SetProperty(m => m.ReadInNotificationFeed, true), cancellationToken);
    }

    public async Task DismissRowAsync(NotificationFeedMarkContext context, JobAdId jobAdId, CancellationToken cancellationToken)
    {
        var db = context.Db;
        var userId = context.UserId;
        // Filters on the dismissed flag, never the read flag: a row read in the bell is dismissed too. Both flags in one
        // statement, with the consent beside them (ADR 0168 D3).
        await db.UserJobAdMatches
            .Where(m => m.UserId == userId && m.JobAdId == jobAdId && !m.DismissedFromNotificationFeed
                        && db.JobSeekers.Where(NotificationConsent.BackgroundMatch.Criteria).Any(js => js.UserId == m.UserId))
            .ExecuteUpdateAsync(set => set
                .SetProperty(m => m.DismissedFromNotificationFeed, true)
                .SetProperty(m => m.ReadInNotificationFeed, true), cancellationToken);
    }

    public async Task MarkThroughAsync(
        NotificationFeedMarkContext context, DateTimeOffset through, CancellationToken cancellationToken)
    {
        var db = context.Db;
        var userId = context.UserId;
        var seeker = await db.JobSeekers
            .Where(js => js.UserId == userId)
            .Select(js => new { js.LastSeenMatchesAt })
            .FirstOrDefaultAsync(cancellationToken);
        if (seeker is null)
            return;

        var watermark = seeker.LastSeenMatchesAt;
        await db.UserJobAdMatches
            .Where(m => m.UserId == userId
                        && m.CreatedAt <= through
                        && (watermark == null || m.CreatedAt > watermark)
                        && !m.ReadInNotificationFeed
                        && db.JobSeekers.Where(NotificationConsent.BackgroundMatch.Criteria).Any(js => js.UserId == m.UserId))
            .ExecuteUpdateAsync(set => set.SetProperty(m => m.ReadInNotificationFeed, true), cancellationToken);
    }

    public async Task MarkBacklogAsync(NotificationFeedMarkContext context, CancellationToken cancellationToken)
    {
        var db = context.Db;
        var userId = context.UserId;
        var stamp = await db.JobSeekers
            .Where(js => js.UserId == userId)
            .Select(js => new { js.Preferences.NotificationFeedMatchesOffAt })
            .FirstOrDefaultAsync(cancellationToken);
        if (stamp?.NotificationFeedMatchesOffAt is not { } offAt)
            return;

        await db.UserJobAdMatches
            .Where(m => m.UserId == userId
                        && m.CreatedAt > offAt
                        && !m.ReadInNotificationFeed
                        && db.JobSeekers.Where(NotificationConsent.BackgroundMatch.Criteria).Any(js => js.UserId == m.UserId))
            .ExecuteUpdateAsync(set => set.SetProperty(m => m.ReadInNotificationFeed, true), cancellationToken);
    }
}
