using System.Net;
using Jobbliggaren.Infrastructure.Auditing;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auditing;

/// <summary>
/// Verifierar IP-maskning per ADR 0024 D7. Samma logik konsumeras av
/// audit-pipelinen (RequestContextProvider) och app-loggen
/// (AuthAuditLogger) — denna testsuit är gemensamt regression-skydd.
/// </summary>
public class IpAnonymizerTests
{
    private readonly IpAnonymizer _sut = new();

    [Theory]
    [InlineData("1.2.3.4", "1.2.3.0")]
    [InlineData("10.0.0.123", "10.0.0.0")]
    [InlineData("203.0.113.255", "203.0.113.0")]
    [InlineData("255.255.255.255", "255.255.255.0")]
    [InlineData("0.0.0.0", "0.0.0.0")]
    public void Ipv4_LastOctetZeroed(string input, string expected)
    {
        _sut.Anonymize(IPAddress.Parse(input)).ShouldBe(expected);
    }

    [Theory]
    [InlineData("2001:db8:1234:5678:90ab:cdef:1234:5678", "2001:db8:1234::")]
    [InlineData("fe80::1", "fe80::")]
    [InlineData("::1", "::")]
    public void Ipv6_LastEightyBitsZeroed(string input, string expected)
    {
        _sut.Anonymize(IPAddress.Parse(input)).ShouldBe(expected);
    }

    [Fact]
    public void Ipv4MappedToIpv6_NormalizedToIpv4_BeforeMasking()
    {
        // ::ffff:1.2.3.4 → 1.2.3.0 (mapped till IPv4 först, sen /24)
        var mapped = IPAddress.Parse("::ffff:1.2.3.4");

        _sut.Anonymize(mapped).ShouldBe("1.2.3.0");
    }
}
