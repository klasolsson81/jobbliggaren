namespace Jobbliggaren.Application.Common.Abstractions;

/// <summary>
/// Civil calendar boundaries in Sweden's time zone, for counters and windows a
/// user reads as "today" or "this month".
///
/// <para>
/// <b>Why this exists (Klas-direktiv 2026-07-28).</b> The landing counter's
/// "nya idag" reset at UTC midnight, which is 01:00 or 02:00 Swedish time. For
/// one to two hours after Swedish midnight it counted mostly yesterday, and ads
/// published between Swedish 00:00 and 02:00 were excluded from "nya idag" for
/// the remaining ~22 hours of that Swedish day. The defect is symmetric, and
/// the ruling is that Jobbliggaren is a Swedish product first: a day boundary a
/// user reads is the Swedish one.
/// </para>
///
/// <para>
/// <b>Why a separate port rather than widening <see cref="Jobbliggaren.Domain.Common.IDateTimeProvider"/>.</b>
/// That interface is consumed in dozens of places across the solution and lives
/// in Domain because Domain genuinely needs it (aggregate factories take it).
/// Exactly three application-layer sites need a civil calendar boundary, and no
/// aggregate needs one — a time zone is a locale concern, not an invariant.
/// Widening a port with many consumers for the sake of a few is the ISP
/// violation this split avoids (CTO-bind 2026-07-28).
/// </para>
///
/// <para>
/// <b>Instants in, instants out — and the returned <c>Offset</c> is ALWAYS
/// <see cref="TimeSpan.Zero"/>.</b> Both members return a
/// <see cref="DateTimeOffset"/> at the UTC instant the Swedish boundary falls
/// on, so a caller compares it directly against a <c>timestamptz</c> column
/// without converting inside the LINQ expression. The zero offset is part of
/// this contract, not an implementation detail: Npgsql writes a
/// <see cref="DateTimeOffset"/> to <c>timestamp with time zone</c> only when
/// the offset is zero, so any implementation must normalise. What varies with
/// DST is the INSTANT, not the offset — Swedish midnight is 23:00Z the
/// previous day in winter and 22:00Z in summer.
/// </para>
/// <para>
/// A consequence worth stating, because a consumer will otherwise be caught by
/// it: the returned value is <b>not</b> the 1st of the month, not the same
/// calendar day the caller asked about, and <b>not necessarily the same year</b>.
/// <c>StartOfMonth(2026, 7)</c> is <c>2026-06-30T22:00Z</c>, and
/// <c>StartOfMonth(2026, 1)</c> is <c>2025-12-31T23:00Z</c> — so reading
/// <c>.Year</c>/<c>.Month</c> off it to label a January bucket yields
/// <i>December 2025</i>. Derive display labels from the ARGUMENTS, never from
/// the return value.
/// </para>
///
/// <para>
/// <b>Midnight is never ambiguous here.</b> EU DST transitions occur at 01:00
/// UTC (02:00/03:00 local), so midnight in Sweden is neither skipped nor
/// repeated. Callers need no invalid-time or ambiguous-time handling, and the
/// implementation deliberately provides none — adding it would imply a case
/// that cannot arise.
/// </para>
/// </summary>
public interface ISwedishCalendar
{
    /// <summary>
    /// The instant at which the Swedish civil day containing <paramref name="instant"/>
    /// began. For 2026-05-23T14:00Z (summer, UTC+2) this is 2026-05-22T22:00Z.
    /// </summary>
    DateTimeOffset StartOfDay(DateTimeOffset instant);

    /// <summary>
    /// The instant at which the Swedish civil month <paramref name="year"/>/<paramref name="month"/>
    /// began. It has NO production consumer yet — the month-windowed
    /// application-statistics surfaces will use it, and do not today.
    ///
    /// <para>
    /// A month can never begin on a DST transition, so this needs no more care
    /// than <see cref="StartOfDay"/>: EU transitions fall on the last Sunday of
    /// March and October, which in a 31-day month is always the 25th or later.
    /// </para>
    /// <para>
    /// <b>Never derive one of these from another with <c>AddMonths</c></b> — not
    /// as a series, and <b>not for a window's exclusive end</b>, which is the
    /// form both prospective call sites actually write (<c>start.AddMonths(1)</c>).
    /// The returned instant is the previous month's last day in UTC, so
    /// <c>StartOfMonth(2026, 7).AddMonths(1)</c> is 30 July, a full day short of
    /// the real August boundary — and across the March transition it is three
    /// days short. Stepping whole months also carries the anchor's own DST
    /// offset into a month with a different one, adding an hour on top.
    /// Ask for the next month instead.
    /// </para>
    /// </summary>
    DateTimeOffset StartOfMonth(int year, int month);
}
