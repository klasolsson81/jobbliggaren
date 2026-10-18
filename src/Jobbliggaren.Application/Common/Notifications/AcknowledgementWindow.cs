namespace Jobbliggaren.Application.Common.Notifications;

/// <summary>
/// The cut a "new since you last looked" page makes over its capped read, and the watermark that visit
/// may acknowledge (#1576). Pure: no I/O, no clock, no state.
///
/// <para>
/// <b>The read it cuts is OLDEST first, at most <c>maxRows + 1</c> rows.</b> The extra row detects
/// truncation without a second count query. Oldest-first is not a preference: the acknowledgement is the
/// max over what is shown, so taking the oldest leaves everything newer above the watermark for the next
/// visit. Taking the newest would acknowledge past the rest and swallow it permanently.
/// </para>
///
/// <para>
/// <b>The window is never cut INSIDE a group of rows that share a timestamp.</b> The read predicate is
/// strict (<c>createdAt &gt; lastSeen</c>), so acknowledging a timestamp that a row beyond the cap also
/// carries would exclude that row FOR GOOD — the watermark only moves forward. The boundary tie group is
/// therefore trimmed out whole, which also means intra-tie order can never decide which row survives, so
/// the read needs no tiebreak. Ties are reachable: <c>CompanyWatchScanJob</c> stamps
/// <c>clock.UtcNow</c> per hit in a tight loop and timestamptz truncates to the microsecond. Worse, the
/// conditions correlate: a window overflows the cap precisely when one scan wrote a burst, which is when
/// neighbouring iterations land in the same microsecond.
/// </para>
///
/// <para>
/// That reach covers a BOUNDARY tie of a few rows. It does not cover the degenerate case, which needs the
/// whole capped window plus one to share a single value: the microsecond is a ceiling on resolution, not a
/// floor on the loop, and each iteration pays a Guid and an EF Add. That branch is a guard, not a live
/// path. In it the whole capped window carries ONE timestamp and more rows share it. A timestamp watermark
/// cannot both show a partial group and acknowledge it, so the visit acknowledges NOTHING
/// (<see cref="Cut{T}.AcknowledgedThrough"/> is <c>null</c>): the user sees the rows and the count stands;
/// the next visit shows the same ones. No progress is the safe direction — silent loss is not.
/// </para>
///
/// <para>
/// <b>The acknowledgement covers every row kept, in the unit of the clock that stamped them</b> —
/// including rows a caller's own filter (the follow rail's grade fork) excludes after the cut.
/// Acknowledging only the included rows would hold the watermark down behind an excluded older row and
/// re-show the same included rows every visit.
/// </para>
/// </summary>
internal static class AcknowledgementWindow
{
    /// <summary>
    /// Cuts <paramref name="oldestFirst"/> — a read ordered oldest first and limited to
    /// <paramref name="maxRows"/> + 1 rows — to the rows a visit shows and the timestamp it may acknowledge.
    /// </summary>
    /// <param name="oldestFirst">The read, ordered by <paramref name="at"/> ascending.</param>
    /// <param name="maxRows">The page's cap, at least 1.</param>
    /// <param name="at">The timestamp the watermark compares against, per row.</param>
    public static Cut<T> Apply<T>(IReadOnlyList<T> oldestFirst, int maxRows, Func<T, DateTimeOffset> at)
    {
        ArgumentNullException.ThrowIfNull(oldestFirst);
        ArgumentNullException.ThrowIfNull(at);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRows, 1);
        for (var i = 1; i < oldestFirst.Count; i++)
        {
            if (at(oldestFirst[i]) < at(oldestFirst[i - 1]))
                throw new ArgumentException("The read must be ordered oldest first.", nameof(oldestFirst));
        }

        if (oldestFirst.Count <= maxRows)
        {
            return new Cut<T>(
                oldestFirst,
                Truncated: false,
                AcknowledgedThrough: oldestFirst.Count == 0 ? null : oldestFirst.Max(at));
        }

        var firstExcluded = at(oldestFirst[maxRows]);
        var capped = oldestFirst.Take(maxRows).ToList();
        var trimmed = capped.Where(r => at(r) < firstExcluded).ToList();

        return trimmed.Count == 0
            ? new Cut<T>(capped, Truncated: true, AcknowledgedThrough: null)
            : new Cut<T>(trimmed, Truncated: true, AcknowledgedThrough: trimmed.Max(at));
    }

    /// <summary>
    /// What a visit shows and acknowledges. <see cref="Rows"/> is empty only when the read was;
    /// <see cref="AcknowledgedThrough"/> is <c>null</c> then, and in the degenerate whole-window tie.
    /// </summary>
    internal sealed record Cut<T>(IReadOnlyList<T> Rows, bool Truncated, DateTimeOffset? AcknowledgedThrough);
}
