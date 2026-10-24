using System.Net;
using Jobbliggaren.Application.Admin.Email.Dmarc;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Email.Dmarc;

// Every address here is from the documentation ranges (RFC 5737, RFC 3849): a source is a third party's data.
public sealed class ReportedSourceAddressTests
{
    [Theory]
    [InlineData("192.0.2.1", "192.0.2.1")]
    [InlineData("198.51.100.255", "198.51.100.255")]
    [InlineData("203.0.113.0", "203.0.113.0")]
    [InlineData("2001:db8::1", "2001:db8::1")]
    [InlineData("2001:DB8:0:0:0:0:0:1", "2001:db8::1")]
    [InlineData("::ffff:192.0.2.7", "192.0.2.7")]
    public void TryParse_AnAddressAReporterWrites_IsKeptInCanonicalForm(string text, string expected)
    {
        ReportedSourceAddress.TryParse(text, out var address).ShouldBeTrue();

        address!.ToString().ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("65536")]
    [InlineData("20.2")]
    [InlineData("192.0.2")]
    [InlineData("192.0.2.1.5")]
    [InlineData("192.0.2.01")]
    [InlineData("192.0.2.256")]
    [InlineData("0x7f.0.0.1")]
    [InlineData(" 192.0.2.1")]
    [InlineData("192.0.2.1 ")]
    [InlineData("2001:db8::1%eth0")]
    [InlineData("2001:db8::1%2")]
    [InlineData("not-an-address")]
    [InlineData("192.0.2.1/24")]
    public void TryParse_AFormThatIsNotOneAddress_IsRefused(string? text)
    {
        ReportedSourceAddress.TryParse(text, out var address).ShouldBeFalse();
        address.ShouldBeNull();
    }

    [Fact]
    public void TryParse_TheSameServerWrittenTwoWays_IsOneSource()
    {
        ReportedSourceAddress.TryParse("192.0.2.9", out var v4).ShouldBeTrue();
        ReportedSourceAddress.TryParse("::ffff:192.0.2.9", out var mapped).ShouldBeTrue();

        mapped.ShouldBe(v4);
    }

    [Fact]
    public void FromStored_AMappedAddress_ComesBackAsIPv4()
    {
        var stored = IPAddress.Parse("::ffff:203.0.113.4");

        ReportedSourceAddress.FromStored(stored).ToString().ShouldBe("203.0.113.4");
    }
}
