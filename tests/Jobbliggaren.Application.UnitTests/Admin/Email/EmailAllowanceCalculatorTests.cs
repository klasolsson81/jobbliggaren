using Jobbliggaren.Application.Admin.Email;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Email;

/// <summary>
/// The arithmetic behind the allowance figure (#1981). The price is 0.25 EUR per 1 000 extra mails before VAT. The
/// rounding cases use 10 kronor per euro and Swedish VAT, so 800 extra mails cost exactly 250 öre and one more crosses
/// to 251; the default rate is pinned separately.
/// </summary>
public class EmailAllowanceCalculatorTests
{
    private const int Included = 300;
    private const decimal Price = 0.25m;
    private const decimal Vat = 0.25m;
    private const decimal TenKronor = 10m;

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
    [InlineData(800, 250)]
    [InlineData(801, 251)]
    [InlineData(1_000, 313)]
    public void ExtraCostOre_ShouldAddVatConvertAndRoundUpOnce_SoTheEstimateNeverUnderstates(long billable, long expectedOre) =>
        EmailAllowanceCalculator.ExtraCostOre(billable, Price, Vat, TenKronor).ShouldBe(expectedOre);

    [Fact]
    public void ExtraCostOre_ShouldAddTheVat()
    {
        EmailAllowanceCalculator.ExtraCostOre(1_000, Price, 0m, TenKronor).ShouldBe(250);
        EmailAllowanceCalculator.ExtraCostOre(1_000, Price, Vat, TenKronor).ShouldBe(313);
    }

    [Theory]
    [InlineData(301, 105)]
    [InlineData(1_001, 349)]
    public void ExtraCostOre_ShouldUseTheDefaultRate(long billable, long expectedOre)
    {
        var terms = new EmailAllowanceOptions();

        EmailAllowanceCalculator.ExtraCostOre(billable, terms.PricePerThousandExtra, terms.VatRate, terms.SekPerEur)
            .ShouldBe(expectedOre);
    }

    [Fact]
    public void ExtraCostOre_ShouldHoldForTheLargestCountTheProviderCanReport()
    {
        // monthly_emails_count is a uint32 on the wire, so this is the ceiling of what the adapter can pass.
        const long largest = uint.MaxValue;

        EmailAllowanceCalculator.ExtraCostOre(largest, Price, Vat, 11.1450m).ShouldBe(1_495_856_579);
    }

    [Fact]
    public void ExtraCostOre_ShouldBeZero_WhenThePriceIsZero() =>
        EmailAllowanceCalculator.ExtraCostOre(10_000, 0m, Vat, TenKronor).ShouldBe(0);

    [Fact]
    public void ExtraCostOre_ShouldRefuseARateOfZero() =>
        Should.Throw<ArgumentOutOfRangeException>(() => EmailAllowanceCalculator.ExtraCostOre(1, Price, Vat, 0m));

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
