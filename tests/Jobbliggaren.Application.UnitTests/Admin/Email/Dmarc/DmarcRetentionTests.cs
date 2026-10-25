using Jobbliggaren.Application.Admin.Email.Dmarc;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Email.Dmarc;

public sealed class DmarcRetentionTests
{
    [Theory]
    [InlineData("2026-10-24T10:00:00+00:00", "2025-10-24T10:00:00+00:00")]
    [InlineData("2028-02-29T12:00:00+00:00", "2027-02-28T12:00:00+00:00")]
    [InlineData("2026-10-24T01:30:00+02:00", "2025-10-23T23:30:00+00:00")]
    public void CutoffOf_IsTwelveCalendarMonthsBefore_InUtc(string now, string expected)
    {
        var cutoff = DmarcRetention.CutoffOf(DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture));

        cutoff.ShouldBe(DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
        cutoff.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void AMessageAcceptedAtAnInstant_IsNotYetPastTheCutoffAtThatInstant()
    {
        var now = new DateTimeOffset(2028, 2, 29, 23, 59, 59, TimeSpan.Zero);
        var received = DmarcRetention.ReceivedAtOf(now, now);

        (received < DmarcRetention.CutoffOf(now)).ShouldBeFalse();
    }

    [Fact]
    public void ReceivedAtOf_NeverLiesInTheFuture()
    {
        var now = new DateTimeOffset(2026, 10, 24, 8, 0, 0, TimeSpan.Zero);

        DmarcRetention.ReceivedAtOf(now.AddHours(5), now).ShouldBe(now);
        DmarcRetention.ReceivedAtOf(now.AddHours(-5), now).ShouldBe(now.AddHours(-5));
    }
}
