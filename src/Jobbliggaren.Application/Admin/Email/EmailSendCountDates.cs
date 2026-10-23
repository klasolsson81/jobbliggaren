using Jobbliggaren.Application.Common.Abstractions;

namespace Jobbliggaren.Application.Admin.Email;

/// <summary>
/// #1981 PR 3 (ADR 0181) — the one date rule of our own send counts. A rolling period is read as every whole Swedish
/// civil date it touches, from <see cref="FirstDateOf"/> through today, so the counts never miss an attempt the period
/// holds. Retention keeps every date the longest period can read by deleting only what lies before
/// <c>FirstDateOf(Last7Days)</c>: one function, so the read window and the deletion cannot drift apart.
/// </summary>
public static class EmailSendCountDates
{
    public static DateOnly FirstDateOf(EmailDeliveryPeriod period, DateTimeOffset now, ISwedishCalendar calendar) =>
        calendar.DayOf(now - EmailDeliveryPeriods.LengthOf(period));
}
