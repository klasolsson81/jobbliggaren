namespace Jobbliggaren.Application.Common.Abstractions;

/// <summary>
/// The half-open instant range <c>[Start, End)</c> of one Swedish civil month,
/// carried TOGETHER with the label it belongs to. The three attributes are
/// co-invariant — <c>Start</c> and <c>End</c> must be the boundaries of exactly
/// <c>Month</c> — which is why they are one value and not a loose pair
/// (Evans 2003, Value Objects).
///
/// <para>
/// <b>Why the label rides along, when the caller just passed it in.</b>
/// <c>Start</c> is the previous month's last day in UTC: <c>2026-06-30T22:00Z</c>
/// for July, and <c>2025-12-31T23:00Z</c> for January, which crosses the YEAR.
/// Reading <c>Start.Year</c>/<c>Start.Month</c> to label a bucket is therefore
/// always wrong, and it is what <c>ApplicationStatsCalculator</c> did before this
/// type existed — a January-2026 bucket rendered as <i>december 2025</i>. The
/// redundancy IS the mechanism: <see cref="Month"/> is redundant at the line that
/// constructs the window and essential at the line that labels the bucket, and in
/// a loop those are different statements. That distance was the defect.
/// </para>
///
/// <para>
/// <b>Why <see cref="End"/> is handed over rather than derived.</b> Deriving it
/// with <c>Start.AddMonths(1)</c> is wrong by the difference in month lengths —
/// and silently RIGHT in seven months of twelve, because <c>AddMonths</c> clamps
/// the day-of-month. Measured over 2026, and stated by the month whose END is
/// being computed — the framing <c>SwedishCalendarTests</c> measures (an earlier
/// draft of this paragraph named the TARGET boundaries instead and listed
/// November in both columns): exact for January, February, April, June, August,
/// September and November; short by <b>2 d 23 h</b> for March (1 d 23 h in a leap
/// year, where the anchor is 29 February); by <b>1 d</b> for May, July and
/// December; by <b>1 d 1 h</b> for October. February is exact in every year, leap
/// or not, and the seven/five split holds every year. A defect invisible 58 % of
/// the year survives any hand check that does not happen to fall in one of the
/// five bad months. In the activity report that means quietly too few rows in the
/// document a job seeker files with Arbetsförmedlingen.
/// </para>
///
/// <para>
/// Both ends come from the adapter and both carry <c>Offset == Zero</c>. That is
/// load-bearing, not cosmetic: these are <c>timestamptz</c> query parameters, and
/// Npgsql writes a <see cref="DateTimeOffset"/> to
/// <c>timestamp with time zone</c> only at offset zero.
/// </para>
/// </summary>
public readonly record struct CivilMonthWindow(
    CivilMonth Month, DateTimeOffset Start, DateTimeOffset End);
