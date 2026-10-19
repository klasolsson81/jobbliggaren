using System.ComponentModel.DataAnnotations;
using Jobbliggaren.Application.Admin.Email;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Email;

/// <summary>
/// The terms are code defaults with no appsettings entry (CTO addendum A, #1981). A merge deploys (ADR 0154), so
/// a default that fails validation must fail here, in CI, and never as a production boot.
/// </summary>
public class EmailAllowanceOptionsTests
{
    private static List<ValidationResult> Validate(EmailAllowanceOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void Defaults_ShouldPassValidation() => Validate(new EmailAllowanceOptions()).ShouldBeEmpty();

    [Fact]
    public void Defaults_ShouldBeTheEssentialTermsReadOn20261018()
    {
        var options = new EmailAllowanceOptions();

        options.Plan.ShouldBe(EmailPlan.Essential);
        options.IncludedMonthlyEmails.ShouldBe(300);
        options.PricePerThousandExtra.ShouldBe(0.25m);
        options.PriceReadOn.ShouldBe(new DateOnly(2026, 10, 18));
        options.MonthlySendLimit.ShouldBe(10_000);
        options.VatRate.ShouldBe(0.25m);
        options.SekPerEur.ShouldBe(11.1450m);
        options.ExchangeRateOn.ShouldBe(new DateOnly(2026, 10, 16));
    }

    [Fact]
    public void Validate_ShouldRefuseTheUnknownPlan() =>
        Validate(new EmailAllowanceOptions { Plan = EmailPlan.Unknown })
            .ShouldContain(r => r.MemberNames.Contains(nameof(EmailAllowanceOptions.Plan)));

    [Fact]
    public void Validate_ShouldRefuseAPlanOutsideTheEnum() =>
        Validate(new EmailAllowanceOptions { Plan = (EmailPlan)99 })
            .ShouldContain(r => r.MemberNames.Contains(nameof(EmailAllowanceOptions.Plan)));

    [Fact]
    public void Validate_ShouldRefuseANegativeIncludedCount() =>
        Validate(new EmailAllowanceOptions { IncludedMonthlyEmails = -1 })
            .ShouldContain(r => r.MemberNames.Contains(nameof(EmailAllowanceOptions.IncludedMonthlyEmails)));

    [Theory]
    [InlineData("-0.01")]
    [InlineData("1000.01")]
    public void Validate_ShouldRefuseAPriceOutsideItsBounds(string price) =>
        Validate(new EmailAllowanceOptions { PricePerThousandExtra = decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture) })
            .ShouldContain(r => r.MemberNames.Contains(nameof(EmailAllowanceOptions.PricePerThousandExtra)));

    [Fact]
    public void Validate_ShouldAcceptAZeroPrice() =>
        Validate(new EmailAllowanceOptions { PricePerThousandExtra = 0m }).ShouldBeEmpty();

    [Fact]
    public void Validate_ShouldRefuseAnUnsetReadDate() =>
        Validate(new EmailAllowanceOptions { PriceReadOn = default })
            .ShouldContain(r => r.MemberNames.Contains(nameof(EmailAllowanceOptions.PriceReadOn)));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_ShouldRefuseASendLimitBelowOne(int limit) =>
        Validate(new EmailAllowanceOptions { MonthlySendLimit = limit })
            .ShouldContain(r => r.MemberNames.Contains(nameof(EmailAllowanceOptions.MonthlySendLimit)));

    [Theory]
    [InlineData("-0.01")]
    [InlineData("1.01")]
    public void Validate_ShouldRefuseAVatRateOutsideAFraction(string rate) =>
        Validate(new EmailAllowanceOptions { VatRate = decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture) })
            .ShouldContain(r => r.MemberNames.Contains(nameof(EmailAllowanceOptions.VatRate)));

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("100.01")]
    public void Validate_ShouldRefuseAnExchangeRateOutsideItsBounds(string rate) =>
        Validate(new EmailAllowanceOptions { SekPerEur = decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture) })
            .ShouldContain(r => r.MemberNames.Contains(nameof(EmailAllowanceOptions.SekPerEur)));

    [Fact]
    public void Validate_ShouldRefuseAnUnsetExchangeRateDate() =>
        Validate(new EmailAllowanceOptions { ExchangeRateOn = default })
            .ShouldContain(r => r.MemberNames.Contains(nameof(EmailAllowanceOptions.ExchangeRateOn)));
}
