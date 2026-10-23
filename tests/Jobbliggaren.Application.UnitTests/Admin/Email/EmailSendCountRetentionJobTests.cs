using Jobbliggaren.Application.Admin.Email;
using Jobbliggaren.Application.Admin.Email.Jobs.EmailSendCountRetention;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure.Time;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Email;

/// <summary>
/// #1981 PR 3 (ADR 0180) — the retention keeps the first date the seven-day period reads and everything after it, by the
/// same rule the read uses.
/// </summary>
public sealed class EmailSendCountRetentionJobTests
{
    private sealed class RecordingRetention : IEmailSendCountRetention
    {
        public List<DateOnly> Cutoffs { get; } = [];

        public Task<int> DeleteBeforeAsync(DateOnly firstKeptDay, CancellationToken cancellationToken)
        {
            Cutoffs.Add(firstKeptDay);
            return Task.FromResult(3);
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    [Theory]
    [InlineData("2026-10-23T04:40:00Z", "2026-10-16")]
    [InlineData("2026-03-29T04:40:00Z", "2026-03-22")]
    [InlineData("2026-10-25T04:40:00Z", "2026-10-18")]
    public async Task RunAsync_KeepsTheFirstDateTheSevenDayPeriodReads(string now, string firstKept)
    {
        var retention = new RecordingRetention();
        var at = DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture);
        var calendar = new SwedishCalendar();
        var job = new EmailSendCountRetentionJob(
            retention, new FixedClock(at), calendar, NullLogger<EmailSendCountRetentionJob>.Instance);

        await job.RunAsync(TestContext.Current.CancellationToken);

        retention.Cutoffs.ShouldBe([DateOnly.Parse(firstKept, System.Globalization.CultureInfo.InvariantCulture)]);
        retention.Cutoffs[0].ShouldBe(EmailSendCountDates.FirstDateOf(EmailDeliveryPeriod.Last7Days, at, calendar));
    }
}
