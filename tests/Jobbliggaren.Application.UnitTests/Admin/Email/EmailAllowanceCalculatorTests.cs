using Jobbliggaren.Application.Admin.Email;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Email;

/// <summary>
/// The arithmetic behind the allowance figure (#1981). The price is 0.25 EUR per 1 000 extra mails, so one
/// extra mail costs 0.025 cent: every boundary below is where the rounding up to the whole cent moves.
/// </summary>
public class EmailAllowanceCalculatorTests
{
    private const int Included = 300;
    private const decimal Price = 0.25m;

    [Theory]
    [InlineData(0, 0)]
    [InlineData(299, 0)]
    [InlineData(300, 0)]
    [InlineData(301, 1)]
    [InlineData(305, 5)]
    [InlineData(1_300, 1_000)]
    public void Billable_ShouldBeWhatIsSentBeyondTheIncludedMails_NeverNegative(long sent, long expected) =>
        EmailAllowanceCalculator.Billable(sent, Included).ShouldBe(expected);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(4, 1)]
    [InlineData(40, 1)]
    [InlineData(41, 2)]
    [InlineData(1_000, 25)]
    [InlineData(1_001, 26)]
    public void ExtraCostCents_ShouldRoundUpToTheWholeCent_SoTheEstimateNeverUnderstates(long billable, long expectedCents) =>
        EmailAllowanceCalculator.ExtraCostCents(billable, Price).ShouldBe(expectedCents);

    [Fact]
    public void ExtraCostCents_ShouldHoldForTheLargestCountTheProviderCanReport()
    {
        // monthly_emails_count is a uint32 on the wire, so this is the ceiling of what the adapter can pass.
        const long largest = uint.MaxValue;

        EmailAllowanceCalculator.ExtraCostCents(largest, Price).ShouldBe(107_374_183);
    }

    [Fact]
    public void ExtraCostCents_ShouldBeZero_WhenThePriceIsZero() =>
        EmailAllowanceCalculator.ExtraCostCents(10_000, 0m).ShouldBe(0);

    [Theory]
    [InlineData(-1)]
    [InlineData(long.MinValue)]
    public void Billable_ShouldRefuseANegativeCount(long sent) =>
        Should.Throw<ArgumentOutOfRangeException>(() => EmailAllowanceCalculator.Billable(sent, Included));

    [Theory]
    [InlineData(0, false)]
    [InlineData(4_999, false)]
    [InlineData(5_000, false)]
    [InlineData(5_001, true)]
    [InlineData(10_000, true)]
    [InlineData(12_000, true)]
    public void IsAboveHalfOfTheSendLimit_ShouldFireStrictlyAboveHalf_AsAdr0161TriggerOneIsWorded(long sent, bool expected) =>
        EmailAllowanceCalculator.IsAboveHalfOfTheSendLimit(sent, 10_000).ShouldBe(expected);

    [Fact]
    public void IsAboveHalfOfTheSendLimit_ShouldFireAboveHalf_ForAnOddLimit()
    {
        EmailAllowanceCalculator.IsAboveHalfOfTheSendLimit(5, 11).ShouldBeFalse();
        EmailAllowanceCalculator.IsAboveHalfOfTheSendLimit(6, 11).ShouldBeTrue();
    }
}
