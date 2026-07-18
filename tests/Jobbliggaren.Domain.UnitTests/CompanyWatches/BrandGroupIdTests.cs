using Jobbliggaren.Domain.CompanyWatches;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.CompanyWatches;

/// <summary>
/// #311 PR-5 (ADR 0087 D4) — format invariants for the <see cref="BrandGroupId"/> slug VO. The
/// default-deny regex is load-bearing: it keeps a brand-group slug from ever colliding with an
/// org.nr shape and pins the <c>brand_group_id varchar(40)</c> column width.
/// </summary>
public class BrandGroupIdTests
{
    [Theory]
    [InlineData("volvo-koncernen")]
    [InlineData("volvo")]
    [InlineData("h-och-m")]
    [InlineData("group2")]
    [InlineData("a")]
    public void Create_WithValidSlug_Succeeds(string slug)
    {
        var result = BrandGroupId.Create(slug);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(slug);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlank_FailsRequired(string? slug)
    {
        var result = BrandGroupId.Create(slug);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("BrandGroupId.Required");
    }

    [Theory]
    [InlineData("Volvo")]            // uppercase
    [InlineData("volvo koncern")]    // space
    [InlineData("volvo_koncern")]    // underscore
    [InlineData("-volvo")]           // leading hyphen
    [InlineData("volvo-")]           // trailing hyphen
    [InlineData("volvo--koncern")]   // double hyphen
    [InlineData("volvö")]            // non-ASCII letter (\w would admit it — the #865 rule)
    [InlineData("volvo\n")]          // newline (\z, not $)
    public void Create_WithMalformedSlug_FailsInvalid(string slug)
    {
        var result = BrandGroupId.Create(slug);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("BrandGroupId.Invalid");
    }

    [Fact]
    public void Create_WithOverLengthSlug_FailsTooLong()
    {
        var slug = new string('a', BrandGroupId.MaxLength + 1);

        var result = BrandGroupId.Create(slug);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("BrandGroupId.TooLong");
    }

    [Fact]
    public void Create_AtMaxLength_Succeeds()
    {
        var slug = new string('a', BrandGroupId.MaxLength);

        BrandGroupId.Create(slug).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Equality_IsByValue()
    {
        BrandGroupId.Create("volvo-koncernen").Value
            .ShouldBe(BrandGroupId.FromTrusted("volvo-koncernen"));
    }
}
