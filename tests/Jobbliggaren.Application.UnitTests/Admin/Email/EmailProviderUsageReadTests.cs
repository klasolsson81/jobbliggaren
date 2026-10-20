using Jobbliggaren.Application.Admin.Email;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Email;

/// <summary>
/// The port's result refuses shapes the provider cannot mean (#1981): a negative count would render "0 av 300"
/// after the calculator's floor, which is a false cost statement, so it must never be constructible.
/// </summary>
public class EmailProviderUsageReadTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 18, 19, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Observed_ShouldRefuseANegativeCount() =>
        Should.Throw<ArgumentOutOfRangeException>(() => new EmailProviderUsageRead.Observed(-1, 0, At));

    [Fact]
    public void Observed_ShouldRefuseANegativeWebhookCount() =>
        Should.Throw<ArgumentOutOfRangeException>(() => new EmailProviderUsageRead.Observed(0, -1, At));

    [Fact]
    public void Observed_ShouldAcceptZeroAndAnUnknownWebhookCount()
    {
        new EmailProviderUsageRead.Observed(0, 0, At).MonthlyEmailsCount.ShouldBe(0);
        new EmailProviderUsageRead.Observed(0, null, At).WebhooksCount.ShouldBeNull();
    }

    [Fact]
    public void Failed_ShouldRefuseNotConfigured_WhichIsAStateAndNotAFailure() =>
        Should.Throw<ArgumentException>(() => new EmailProviderUsageRead.Failed(EmailProviderReadReason.NotConfigured));

    [Fact]
    public void Failed_ShouldRefuseAReasonOutsideTheEnum() =>
        Should.Throw<ArgumentException>(() => new EmailProviderUsageRead.Failed((EmailProviderReadReason)99));

    [Theory]
    [InlineData(1L, null)]
    [InlineData(null, 349L)]
    public void TheObservedUsageDto_ShouldRefuseBillableWithoutItsCost_OrTheReverse(long? billable, long? costOre) =>
        Should.Throw<ArgumentException>(() => EmailAllowanceUsageDto.Observed(At, "2026-10", 301, billable, costOre, false, 0));

    [Fact]
    public void TheObservedUsageDto_ShouldAcceptBoth_OrNeither()
    {
        EmailAllowanceUsageDto.Observed(At, "2026-10", 301, 1, 1, false, 0).EstimatedExtraCostOre.ShouldBe(1);
        EmailAllowanceUsageDto.Observed(At, "2026-10", 301, null, null, false, 0).Billable.ShouldBeNull();
    }

    [Fact]
    public void AFailedPlanRead_ShouldRefuseNotConfiguredAndAReasonOutsideTheEnum()
    {
        Should.Throw<ArgumentException>(() => new EmailProviderPlanRead.Failed(EmailProviderReadReason.NotConfigured));
        Should.Throw<ArgumentException>(() => new EmailProviderPlanRead.Failed((EmailProviderReadReason)99));
    }
}
