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
/// user reads is the Swedish one. The same ruling reaches the two month-windowed
/// surfaces, which moved together in the follow-up ADR 0064's amendment named
/// (<c>GetActivityReportQueryHandler</c> and <c>ApplicationStatsCalculator</c>).
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
/// <b>Instants out, and every instant this port returns carries
/// <c>Offset == <see cref="TimeSpan.Zero"/></c>.</b> The values are the UTC
/// instants the Swedish boundaries fall on, so a caller compares them directly
/// against a <c>timestamptz</c> column without converting inside the LINQ
/// expression. The zero offset is part of this contract, not an implementation
/// detail: Npgsql writes a <see cref="DateTimeOffset"/> to
/// <c>timestamp with time zone</c> only when the offset is zero, so any
/// implementation must normalise. What varies with DST is the INSTANT, not the
/// offset — Swedish midnight is 23:00Z the previous day in winter and 22:00Z in
/// summer.
/// </para>
/// <para>
/// <b>A boundary instant is not a label, and this port keeps the two apart in the
/// type system rather than in prose</b> (CTO-bind 2026-07-28-B).
/// <see cref="CivilMonthWindow.Start"/> for July 2026 is <c>2026-06-30T22:00Z</c>,
/// and for January 2026 it is <c>2025-12-31T23:00Z</c> — the wrong month, and the
/// wrong YEAR. An earlier revision of this port returned that instant bare from a
/// <c>StartOfMonth(int, int)</c> member and forbade the misuse in a doc comment.
/// Both prospective consumers already CARRIED the forbidden forms — written
/// against UTC anchors, where they were correct — so the prohibitions were aimed
/// at code that existed and read as right. Labels now come
/// from <see cref="CivilMonth"/>, which cannot be an instant, and windows come
/// from <see cref="CivilMonthWindow"/>, which carries its own label.
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
    /// The Swedish civil month <paramref name="instant"/> falls in — the member a
    /// "current month" default needs, and the one thing the other two cannot be
    /// assembled into.
    ///
    /// <para>
    /// <b>Neither shortcut works, and both fail rarely enough to survive a spot
    /// check.</b> Reading <c>.Year</c>/<c>.Month</c> off the UTC instant is wrong
    /// for the first one to two hours of every Swedish month — 00:30 on 1 August
    /// in Sweden is 31 July in UTC. Reading them off <see cref="StartOfDay"/>'s
    /// return is wrong on the FIRST of every month, all day: that value is the
    /// previous day's 22:00Z or 23:00Z, so on 1 June it reports May, and on
    /// 1 January it reports December 2025. Twelve days a year, and for the
    /// activity report they are precisely the days a report gets filed.
    /// </para>
    /// </summary>
    CivilMonth MonthOf(DateTimeOffset instant);

    /// <summary>
    /// The half-open instant range <c>[Start, End)</c> of the Swedish civil month
    /// <paramref name="month"/>, with the label carried alongside.
    ///
    /// <para>
    /// <b>This replaces the earlier <c>StartOfMonth(int, int)</c>, which handed
    /// back a bare boundary instant and had no production consumer.</b> Both
    /// prospective consumers wrote <c>start.AddMonths(1)</c> for the exclusive
    /// end. That is wrong by the difference in month lengths — never by anything
    /// to do with DST, which only moves the boundary HOUR — and, because
    /// <c>AddMonths</c> clamps the day-of-month, it is <b>silently correct in
    /// seven months of twelve</b>. Measured over 2026:
    /// </para>
    /// <para>
    /// correct for Jan, Feb, Apr, Jun, Aug, Sep and Nov · short by <b>2 d 23 h</b>
    /// for March (28 February is the anchor, against a real 31 March; the
    /// spring-forward accounts only for the missing hour) · short by <b>1 d</b>
    /// for May, July and December · short by <b>1 d 1 h</b> for October. February
    /// is exact in every year, leap or not.
    /// </para>
    /// <para>
    /// So a hand check returns green unless it lands in one of the FIVE bad
    /// months (March, May, July, October, December) — the defect is invisible
    /// 58 % of the year. What it does when it fires is drop rows from the
    /// document a job seeker files with Arbetsförmedlingen.
    /// </para>
    /// <para>
    /// <b>Handing both ends over makes the derivation UNNECESSARY, not
    /// impossible</b> — and the difference is worth stating, because an earlier
    /// revision of this paragraph claimed the stronger thing.
    /// <c>window.Start.AddMonths(1)</c> still compiles; every
    /// <see cref="DateTimeOffset"/> carries <c>.AddMonths</c> and
    /// <c>.Year</c>/<c>.Month</c>, and <see cref="StartOfDay"/> still hands back
    /// a bare boundary instant whose label trap is unguarded. What changed is
    /// that the correct value now sits in the same value as the dangerous one, on
    /// a shorter path, so no consumer needs the derivation. Making it genuinely
    /// unrepresentable would mean wrapping <c>Start</c>/<c>End</c> in a type
    /// without <c>.Month</c>, which breaks EF translation — a real cost, weighed
    /// and not paid.
    /// </para>
    /// <para>
    /// A month can never begin on a DST transition, so this needs no more care
    /// than <see cref="StartOfDay"/>: EU transitions fall on the last Sunday of
    /// March and October, which in a 31-day month is always the 25th or later.
    /// </para>
    /// </summary>
    CivilMonthWindow MonthWindow(CivilMonth month);
}
