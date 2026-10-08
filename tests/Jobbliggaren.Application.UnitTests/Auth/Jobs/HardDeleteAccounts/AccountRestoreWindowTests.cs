using System.Globalization;
using Jobbliggaren.Application.Auth.Jobs.HardDeleteAccounts;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth.Jobs.HardDeleteAccounts;

public sealed class AccountRestoreWindowTests
{
    [Theory]
    [InlineData("2026-10-08T03:59:00Z", "2026-11-07T03:59:00Z", "2026-11-07T04:00:00Z")]
    [InlineData("2026-10-08T04:01:00Z", "2026-11-07T04:01:00Z", "2026-11-08T04:00:00Z")]
    public void From_ShouldProduceTheServerPreviewAndReceiptSnapshots_WhenTheClockCrossesFourUtc(
        string deletedAt, string eligibleAt, string scheduledRunAt)
    {
        var actual = AccountDeletionTiming.From(Parse(deletedAt));

        actual.DeletedAt.ShouldBe(Parse(deletedAt));
        actual.EligibleAt.ShouldBe(Parse(eligibleAt));
        actual.ScheduledRunAt.ShouldBe(Parse(scheduledRunAt));
    }

    [Fact]
    public void EligibleAt_ShouldEndTheThirtyDayRespite_WhenDeletionCrossesTheYearBoundary()
    {
        var deletedAt = new DateTimeOffset(2026, 12, 15, 23, 59, 59, TimeSpan.Zero);

        AccountRestoreWindow.EligibleAt(deletedAt)
            .ShouldBe(new DateTimeOffset(2027, 1, 14, 23, 59, 59, TimeSpan.Zero));
        HardDeleteAccountsJob.RestoreWindowDays.ShouldBe(30);
    }

    [Fact]
    public void EligibleAt_ShouldPreserveHistoricalDeletionPrecision_WhenTheOriginalWriterStoredMicroseconds()
    {
        // Before #1977, DeleteAccountCommandHandler wrote the clock instant; PostgreSQL retained microseconds.
        // The current write is pinned by ScheduleAsync_ShouldUseOneMillisecondInstantForTheEntireCascade_WhenTheClockHasSubMillisecondPrecision.
        var historicalDeletedAt = new DateTimeOffset(2026, 10, 8, 3, 59, 59, TimeSpan.Zero).AddTicks(1_234_560);

        AccountRestoreWindow.EligibleAt(historicalDeletedAt)
            .ShouldBe(new DateTimeOffset(2026, 11, 7, 3, 59, 59, TimeSpan.Zero).AddTicks(1_234_560));
    }

    [Theory]
    [InlineData("2026-11-07T03:59:59.9999999+00:00", "2026-11-07T04:00:00+00:00")]
    [InlineData("2026-11-07T04:00:00+00:00", "2026-11-08T04:00:00+00:00")]
    [InlineData("2026-11-07T04:00:00.0000001+00:00", "2026-11-08T04:00:00+00:00")]
    [InlineData("2026-12-31T23:59:59+00:00", "2027-01-01T04:00:00+00:00")]
    [InlineData("2028-02-29T04:00:00+00:00", "2028-03-01T04:00:00+00:00")]
    public void FirstScheduledRunAfter_ShouldSelectTheFirstStrictlyLaterFourUtcRun_WhenTheRespiteEnds(
        string eligibleAt, string expectedRun)
    {
        var actual = AccountRestoreWindow.FirstScheduledRunAfter(Parse(eligibleAt));

        actual.ShouldBe(Parse(expectedRun));
        actual.Offset.ShouldBe(TimeSpan.Zero);
    }

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.None);
}
