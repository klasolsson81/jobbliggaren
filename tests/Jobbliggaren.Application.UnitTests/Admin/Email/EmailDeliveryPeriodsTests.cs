using System.Text.Json;
using Jobbliggaren.Application.Admin.Email;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Email;

/// <summary>
/// The delivery period's one vocabulary (#1981 PR 2): three tokens in, by ordinal match, and the same three out on the
/// wire; everything else is no period at all, which the validator turns into a 400.
/// </summary>
public class EmailDeliveryPeriodsTests
{
    [Theory]
    [InlineData("h24", EmailDeliveryPeriod.Last24Hours)]
    [InlineData("d3", EmailDeliveryPeriod.Last3Days)]
    [InlineData("d7", EmailDeliveryPeriod.Last7Days)]
    public void FromToken_ShouldNameThePeriod_ForExactlyItsToken(string token, EmailDeliveryPeriod expected) =>
        EmailDeliveryPeriods.FromToken(token).ShouldBe(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("H24")]
    [InlineData("h24 ")]
    [InlineData("42")]
    [InlineData("0")]
    [InlineData("d30")]
    [InlineData("Last24Hours")]
    [InlineData("last24hours")]
    public void FromToken_ShouldBeNull_ForAnythingElse(string? token) =>
        EmailDeliveryPeriods.FromToken(token).ShouldBeNull();

    [Theory]
    [InlineData(EmailDeliveryPeriod.Last24Hours, 24)]
    [InlineData(EmailDeliveryPeriod.Last3Days, 72)]
    [InlineData(EmailDeliveryPeriod.Last7Days, 168)]
    public void LengthOf_ShouldBeTheRollingWindow(EmailDeliveryPeriod period, int hours) =>
        EmailDeliveryPeriods.LengthOf(period).ShouldBe(TimeSpan.FromHours(hours));

    [Fact]
    public void LengthOf_ShouldRefuseAnUndefinedPeriod() =>
        Should.Throw<ArgumentOutOfRangeException>(() => EmailDeliveryPeriods.LengthOf((EmailDeliveryPeriod)42));

    [Theory]
    [InlineData(EmailDeliveryPeriod.Last24Hours, "\"h24\"")]
    [InlineData(EmailDeliveryPeriod.Last3Days, "\"d3\"")]
    [InlineData(EmailDeliveryPeriod.Last7Days, "\"d7\"")]
    public void ThePeriod_ShouldReachTheWireAsItsToken(EmailDeliveryPeriod period, string json)
    {
        JsonSerializer.Serialize(period).ShouldBe(json);
        JsonSerializer.Serialize(period, JsonSerializerOptions.Web).ShouldBe(json);
    }

    [Fact]
    public void TheLimits_ShouldBeAThirtyTwoDayCanary_AndTenFailures()
    {
        EmailDeliveryLimits.SentinelWindow.ShouldBe(TimeSpan.FromDays(32));
        EmailDeliveryLimits.MaxRecentFailures.ShouldBe(10);
    }
}
