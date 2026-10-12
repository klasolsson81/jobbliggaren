using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.JobAds;

namespace Jobbliggaren.Application.NotificationFeed.Sources;

/// <summary>
/// The write side of one bell source (#2130, ADR 0162 as amended): marking its rows read IN THE BELL. A source
/// implements both this and <see cref="INotificationFeedSource"/>, so the way a row is counted, listed and marked
/// lives in one class and a new source adds one class, not a switch in a handler.
///
/// <para>
/// <b>What a mark is.</b> It sets the source row's own <c>ReadInNotificationFeed</c> flag and nothing else: never a
/// watermark (<c>LastSeenMatchesAt</c>, <c>LastSeenFollowedAdsAt</c>), never <c>SeenAt</c>, <c>NotificationStatus</c>
/// or <c>SentAt</c>, never <c>job_seekers</c>. So a mark cannot change what Översikt or the list pages show, and the
/// writer-replay guard (<c>JobSeekerWriterReplayGuardTests</c>) holds: no handler that calls a mark ever calls a
/// <c>JobSeeker</c> mutator. Every member is owner-scoped by <see cref="NotificationFeedMarkContext.UserId"/> and
/// idempotent: a row already read, missing, foreign or ended is a no-op, so the caller can answer 204 for all of
/// them and reveal nothing (IDOR).
/// </para>
///
/// <para>
/// <b>The gate.</b> A mark on a source whose rows were produced under a consent applies that consent in the same
/// statement as the write (an <c>EXISTS</c> over the user's <c>JobSeeker</c>, not a read before it), so a mark never
/// writes a row the bell could not show. It does <b>not</b> apply the user's switch for the type: the backlog mark
/// runs while the switch is still off, and a mark under the type's own gate would always do nothing. A mark that
/// the gate refuses is the same no-op as any other, never an error.
/// </para>
/// </summary>
internal interface INotificationFeedReadMarker
{
    NotificationFeedSourceKind Kind { get; }

    /// <summary>
    /// ONE notification read: the row of <paramref name="jobAdId"/>, which for a source with several rows per ad is
    /// every row of that ad. The bell's row is the (source, ad) pair, so a click reads the whole pair. A tracked load
    /// and the aggregate's own method, committed by the unit of work: a handful of rows at most, and seedable
    /// without a database.
    /// </summary>
    Task MarkRowAsync(NotificationFeedMarkContext context, JobAdId jobAdId, CancellationToken cancellationToken);

    /// <summary>
    /// "Markera alla som lästa": every row that is new since the source's watermark, not yet read, and no newer than
    /// <paramref name="through"/>, the token the source returned. A row that arrived after the token is neither
    /// counted nor marked, so it stays unread for the next read (I4). Set-based, because the counted set is not
    /// bounded by the 20 rows on screen. Reads the watermark; never moves it.
    /// </summary>
    Task MarkThroughAsync(NotificationFeedMarkContext context, DateTimeOffset through, CancellationToken cancellationToken);

    /// <summary>
    /// What arrived while the user had this type switched off, marked read: every row created after the type's
    /// off-stamp. Run when the type is switched back ON and BEFORE the stamp is cleared, so switching on does not
    /// flood the bell with the period it was off. Reads the stamp itself; no stamp (the type is on) is a no-op, and
    /// <c>null</c> never means "since forever". Rows from before the stamp are untouched: they were notifications
    /// before the switch-off and stay as they were.
    /// </summary>
    Task MarkBacklogAsync(NotificationFeedMarkContext context, CancellationToken cancellationToken);
}

/// <summary>
/// What a mark needs for one request. No grading ports on purpose: a mark never decides what matches or ranks, so
/// its inputs cannot become inputs to matching, grading or sorting (security V10).
/// </summary>
internal sealed record NotificationFeedMarkContext(IAppDbContext Db, Guid UserId);
