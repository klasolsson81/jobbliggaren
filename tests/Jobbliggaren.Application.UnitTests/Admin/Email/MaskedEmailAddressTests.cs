using System.Reflection;
using Jobbliggaren.Application.Admin.Email;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Email;

/// <summary>
/// The masked recipient (#1981 PR 2, security-auditor 4c, dotnet-architect Q6): the rule table, and that the mask is
/// total, so no provider value can make it throw or hand back the address it was given. Every address is on a domain
/// RFC 2606/6761 reserves.
/// </summary>
public class MaskedEmailAddressTests
{
    [Theory]
    [InlineData("zelda@example.com", "z•••@example.com")]
    [InlineData("Z@example.org", "Z•••@example.org")]
    [InlineData("7even@example.net", "7•••@example.net")]
    [InlineData("åsa.ek@example.org", "å•••@example.org")]
    [InlineData("\"quoted\"@example.com", "•••@example.com")]
    [InlineData(".dot@example.com", "•••@example.com")]
    [InlineData("+tag@example.com", "•••@example.com")]
    [InlineData("a@b@example.com", "a•••@example.com")]
    [InlineData("a@sub-domain.example.test", "a•••@sub-domain.example.test")]
    [InlineData("a@EXAMPLE.COM", "a•••@EXAMPLE.COM")]
    public void Of_ShouldShowTheFirstLetterOrDigit_AFixedMask_AndTheWholeDomain(string address, string expected) =>
        MaskedEmailAddress.Of(address).Value.ShouldBe(expected);

    [Theory]
    [InlineData("")]
    [InlineData("@")]
    [InlineData("a@")]
    [InlineData("@example.com")]
    [InlineData("no-at-sign.example.com")]
    [InlineData("a@exa mple.com")]
    [InlineData("a@example.com‮evil")]
    [InlineData("a@exa​mple.com")]
    [InlineData("a@example.com\n")]
    [InlineData("a@example.com\t")]
    [InlineData("a@example_com")]
    public void Of_ShouldHideTheWholeAddress_WhenThereIsNoDomainToShowWhole(string address) =>
        MaskedEmailAddress.Of(address).Value.ShouldBe(MaskedEmailAddress.Hidden);

    [Fact]
    public void Of_ShouldHideTheWholeAddress_WhenTheDomainHoldsALoneSurrogate() =>
        MaskedEmailAddress.Of("a@exam\uD800ple.com").Value.ShouldBe(MaskedEmailAddress.Hidden);

    [Fact]
    public void Of_ShouldHideAnAddressOverTheLongestOne()
    {
        var local = new string('a', MaskedEmailAddress.MaxAddressLength - "@example.com".Length + 1);

        MaskedEmailAddress.Of(local + "@example.com").Value.ShouldBe(MaskedEmailAddress.Hidden);
        MaskedEmailAddress.Of(local[1..] + "@example.com").Value.ShouldBe("a•••@example.com");
    }

    [Fact]
    public void Of_ShouldHideADomainOverTheLongestOne() =>
        MaskedEmailAddress.Of("a@" + new string('d', 250) + ".com").Value.ShouldBe(MaskedEmailAddress.Hidden);

    [Fact]
    public void Of_ShouldShowAValidDomainOfTheLongestLength_Whole_AndHideOneCharacterLonger()
    {
        // No label over 63 characters, so the domain is one DNS could hold.
        static string Domain(int fourthLabel) => string.Join(
            '.', new string('a', 63), new string('b', 63), new string('c', 63), new string('d', fourthLabel), "example", "com");
        Domain(49).Length.ShouldBe(253);

        MaskedEmailAddress.Of("z@" + Domain(49)).Value.ShouldBe("z•••@" + Domain(49));
        MaskedEmailAddress.Of("z@" + Domain(50)).Value.ShouldBe(MaskedEmailAddress.Hidden);
    }

    [Fact]
    public void Of_ShouldNeverShowALoneSurrogate_AsTheFirstCharacter() =>
        MaskedEmailAddress.Of("\uD800bc@example.com").Value.ShouldBe("•••@example.com");

    [Fact]
    public void Of_ShouldShowASupplementaryLetterWhole() =>
        MaskedEmailAddress.Of("\U0001D400x@example.com").Value.ShouldBe("\U0001D400•••@example.com");

    [Fact]
    public void Of_ShouldBeTotal_ForANullAndAVeryLongInput()
    {
        MaskedEmailAddress.Of((string?)null).Value.ShouldBe(MaskedEmailAddress.Hidden);
        MaskedEmailAddress.Of(new string('x', 10_000) + "@example.com").Value.ShouldBe(MaskedEmailAddress.Hidden);
    }

    [Fact]
    public void TheMask_ShouldNotShowTheLocalPartsLength()
    {
        MaskedEmailAddress.Of("a@example.com").ShouldBe(MaskedEmailAddress.Of("abcdefghijklmnop@example.com"));
    }

    [Fact]
    public void ToString_ShouldBeTheMaskedValue() =>
        MaskedEmailAddress.Of("zelda@example.com").ToString().ShouldBe("z•••@example.com");

    [Fact]
    public void TheHiddenAddress_ShouldBeTheMaskAlone() =>
        MaskedEmailAddress.HiddenAddress.Value.ShouldBe(MaskedEmailAddress.Hidden);

    [Fact]
    public void ThereShouldBeNoPublicConstructor() =>
        typeof(MaskedEmailAddress).GetConstructors(BindingFlags.Public | BindingFlags.Instance).ShouldBeEmpty();
}
