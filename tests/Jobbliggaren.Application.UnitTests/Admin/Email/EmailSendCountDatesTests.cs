using Jobbliggaren.Application.Admin.Email;
using Jobbliggaren.Infrastructure.Time;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Email;

/// <summary>
/// #1981 PR 3 (ADR 0181, security-auditor condition 3) — the one date rule, against the real Stockholm calendar at every
/// quarter-hour of 2026, both daylight-saving changes included. The read covers its whole rolling period; a purge at the
/// same instant keeps every date any period reads; the purge's cutoff never moves backwards; and at the scheduled run
/// (04:40 UTC) it keeps exactly today and the seven dates before it, which is what makes "eight dates" a measured bound.
/// </summary>
public sealed class EmailSendCountDatesTests
{
    private static readonly SwedishCalendar Calendar = new();

    private static IEnumerable<DateTimeOffset> EveryQuarterHourOf2026()
    {
        for (var at = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
             at.Year == 2026;
             at = at.AddMinutes(15))
            yield return at;
    }

    [Fact]
    public void FirstDateOf_StartsAtOrBeforeThePeriodsOwnStart_AtEveryInstant()
    {
        foreach (var now in EveryQuarterHourOf2026())
        {
            foreach (var period in Enum.GetValues<EmailDeliveryPeriod>())
            {
                var first = EmailSendCountDates.FirstDateOf(period, now, Calendar);
                Calendar.DayWindow(first).Start.ShouldBeLessThanOrEqualTo(
                    now - EmailDeliveryPeriods.LengthOf(period), $"{period} at {now:O}");
            }
        }
    }

    [Fact]
    public void APurgeAtTheSameInstant_KeepsEveryDateAnyPeriodReads()
    {
        foreach (var now in EveryQuarterHourOf2026())
        {
            var cutoff = EmailSendCountDates.FirstDateOf(EmailDeliveryPeriod.Last7Days, now, Calendar);
            foreach (var period in Enum.GetValues<EmailDeliveryPeriod>())
            {
                EmailSendCountDates.FirstDateOf(period, now, Calendar)
                    .ShouldBeGreaterThanOrEqualTo(cutoff, $"{period} at {now:O}");
            }
        }
    }

    [Fact]
    public void TheCutoff_NeverMovesBackwards()
    {
        DateOnly? previous = null;
        foreach (var now in EveryQuarterHourOf2026())
        {
            var cutoff = EmailSendCountDates.FirstDateOf(EmailDeliveryPeriod.Last7Days, now, Calendar);
            if (previous is { } earlier)
                cutoff.ShouldBeGreaterThanOrEqualTo(earlier, $"at {now:O}");
            previous = cutoff;
        }
    }

    [Fact]
    public void AtTheScheduledRun_ThePurgeKeepsTodayAndTheSevenDatesBeforeIt()
    {
        for (var day = new DateOnly(2026, 1, 1); day.Year == 2026; day = day.AddDays(1))
        {
            var run = new DateTimeOffset(day.ToDateTime(new TimeOnly(4, 40)), TimeSpan.Zero);

            EmailSendCountDates.FirstDateOf(EmailDeliveryPeriod.Last7Days, run, Calendar)
                .ShouldBe(Calendar.DayOf(run).AddDays(-7), $"the run at {run:O}");
        }
    }
}
