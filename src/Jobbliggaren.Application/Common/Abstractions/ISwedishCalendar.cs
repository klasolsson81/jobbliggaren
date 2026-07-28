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
/// <b>Instants in, instants out.</b> Both members return a
/// <see cref="DateTimeOffset"/> at the UTC instant the Swedish boundary falls
/// on, so a caller compares it directly against a <c>timestamptz</c> column
/// without converting inside the LINQ expression. The offset varies with DST:
/// Swedish midnight is 23:00Z the previous day in winter and 22:00Z in summer.
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
    /// <b>Do not reproduce a series of these with <c>AddMonths</c>.</b>
    /// <c>AddMonths</c> preserves the offset, so one summer anchor stepped back
    /// six months yields a January instant an hour wrong. Ask for each month.
    /// </para>
    /// </summary>
    DateTimeOffset StartOfMonth(int year, int month);
}
