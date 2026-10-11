using Jobbliggaren.Application.Matching.Queries;
using Jobbliggaren.Application.NotificationFeed.Queries.GetMyNotificationFeed;
using Jobbliggaren.Domain.JobSeekers;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.NotificationFeed.Sources;

/// <summary>
/// The background-match source (#1666): <see cref="NewMatchSet"/> under <c>LastSeenMatchesAt</c>, the
/// same definition as the uncapped new-match count (ADR 0080 Beslut 6), so the bell's number is that
/// count bounded by its token.
///
/// <para>
/// <b>Consent (ADR 0080 Beslut 5, GDPR Art. 7(3)):</b> match rows are produced under the
/// background-match consent, and a withdrawal leaves them in place. The bell is a notification surface,
/// so it reads this source only while <see cref="NotificationConsent.BackgroundMatch"/> holds; without it
/// the source says nothing. The gate is the source's, not the set's: <c>/matchningar</c> and the count
/// keep reading the rows (security-auditor, #1666 form round).
/// </para>
///
/// <para>
/// No horizon: the list is the source's exact newest rows, so nothing newer can be missing from it.
/// </para>
/// </summary>
internal sealed class MatchFeedSource : INotificationFeedSource
{
    public NotificationFeedSourceKind Kind => NotificationFeedSourceKind.Match;

    public async Task<NotificationSourceRead> ReadAsync(
        NotificationFeedReadContext context, CancellationToken cancellationToken)
    {
        var db = context.Db;
        var userId = context.UserId;

        var seeker = await db.JobSeekers
            .Where(NotificationConsent.BackgroundMatch.Criteria)
            .Where(js => js.UserId == userId)
            .Select(js => new { js.LastSeenMatchesAt })
            .FirstOrDefaultAsync(cancellationToken);
        if (seeker is null)
            return NotificationSourceRead.Nothing(Kind);

        var watermark = seeker.LastSeenMatchesAt;
        var unreadSet = NewMatchSet.Matches(db, userId, watermark);

        // Token first, then everything bounded by it: a match committed between these statements is
        // neither counted nor acknowledged, so it stays unread for the next poll.
        var token = await unreadSet.MaxAsync(m => (DateTimeOffset?)m.CreatedAt, cancellationToken);
        var unreadCount = token is { } through
            ? await unreadSet.Where(m => m.CreatedAt <= through).CountAsync(cancellationToken)
            : 0;

        // Nothing unread and never acknowledged means no match exists at all.
        if ((token ?? watermark) is not { } listBound)
            return new NotificationSourceRead(Kind, unreadCount, token, [], null);

        var rows = await (
                from m in NewMatchSet.Matches(db, userId, since: null)
                where m.CreatedAt <= listBound
                join j in db.JobAds.AsNoTracking() on m.JobAdId equals j.Id
                orderby m.CreatedAt descending, m.Id
                select new
                {
                    m.JobAdId,
                    j.Title,
                    Company = j.Company.Name,
                    m.Grade,
                    m.CreatedAt,
                })
            .Take(context.MaxItems)
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(r => new NotificationFeedItemDto(
                Kind,
                r.JobAdId.Value,
                r.Title,
                r.Company,
                r.CreatedAt,
                IsUnread: watermark is not { } seen || r.CreatedAt > seen,
                r.Grade))
            .ToList();

        return new NotificationSourceRead(Kind, unreadCount, token, items, Horizon: null);
    }
}
