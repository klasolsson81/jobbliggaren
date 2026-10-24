using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #1981 PR 4a (ADR 0183): a DMARC report's source address is personal data when the server is a person's own
/// (C-582/14 <i>Breyer</i>), and the repository is public (CLAUDE.md §9.2 (c)). Every IP address written in a test
/// source whose path names DMARC is therefore from the documentation ranges, RFC 5737 (192.0.2.0/24, 198.51.100.0/24,
/// 203.0.113.0/24) and RFC 3849 (2001:db8::/32), so a fixture copied from a real report or from the dmarc@ mailbox
/// fails here. A loopback endpoint is written as <see cref="IPAddress.Loopback"/>, never as a literal.
/// </summary>
public sealed partial class DmarcFixtureAddressGuardTests
{
    private static readonly string[] TextExtensions = [".cs", ".xml", ".json", ".eml", ".txt"];

    [Fact]
    public void Every_address_in_a_dmarc_test_source_is_from_the_documentation_ranges()
    {
        var offenders = DmarcTestSources()
            .SelectMany(file => FixtureAddressScan.Addresses(File.ReadAllText(Path.Combine(RepoRoot(), file)))
                .Where(address => !FixtureAddressScan.IsDocumentationAddress(address))
                .Select(address => $"{file}: {address}"))
            .ToList();

        offenders.ShouldBeEmpty(
            "A DMARC test source writes an address outside the documentation ranges: " + string.Join(", ", offenders));
    }

    [Fact]
    public void The_scan_reaches_the_decoder_tests_and_finds_their_addresses()
    {
        const string decoderTests = "tests/Jobbliggaren.Application.UnitTests/Email/Dmarc/XmlDmarcReportDecoderTests.cs";

        DmarcTestSources().ShouldContain(decoderTests);
        FixtureAddressScan.Addresses(File.ReadAllText(Path.Combine(RepoRoot(), decoderTests))).ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData("\"192.0.2.1\"")]
    [InlineData("\"198.51.100.255\"")]
    [InlineData("\"203.0.113.0/24\"")]
    [InlineData("\"2001:db8::1\"")]
    [InlineData("\"2001:DB8:0:0:0:0:0:1\"")]
    [InlineData("\"::ffff:192.0.2.7\"")]
    [InlineData("<source_ip>2001:db8:4860::7</source_ip>")]
    public void A_documentation_address_passes(string source)
    {
        var addresses = FixtureAddressScan.Addresses(source);

        addresses.ShouldNotBeEmpty();
        addresses.ShouldAllBe(address => FixtureAddressScan.IsDocumentationAddress(address));
    }

    [Theory]
    [InlineData("\"10.1.2.3\"")]
    [InlineData("\"192.0.3.1\"")]
    [InlineData("\"198.51.101.1\"")]
    [InlineData("<source_ip>203.0.114.9</source_ip>")]
    [InlineData("\"2001:db9::1\"")]
    [InlineData("\"fe80::1%1\"")]
    [InlineData("\"::ffff:10.1.2.3\"")]
    [InlineData("\"::1\"")]
    [InlineData("// a server at 10.1.2.3.")]
    public void Any_other_address_is_flagged(string source)
    {
        FixtureAddressScan.Addresses(source)
            .ShouldContain(address => !FixtureAddressScan.IsDocumentationAddress(address));
    }

    [Theory]
    [InlineData("TimeSpan.Parse(\"01:30:00\")")]
    [InlineData("\"2026-10-24T10:00:00Z\"")]
    [InlineData("<version>1.0</version>")]
    [InlineData("\"192.0.2\"")]
    [InlineData("\"65536\"")]
    [InlineData("\"192.0.2.1.5\"")]
    [InlineData("\"999.1.1.1\"")]
    [InlineData("global::System.Net.IPAddress.Loopback")]
    [InlineData("\"1792713600.0123\"")]
    public void Text_that_is_not_an_address_is_not_read_as_one(string source)
    {
        FixtureAddressScan.Addresses(source).ShouldBeEmpty();
    }

    // Repo-relative, forward slashes. This file is left out: its own flagged examples are not fixtures.
    private static List<string> DmarcTestSources([CallerFilePath] string thisFile = "")
    {
        var root = RepoRoot();
        return Directory.EnumerateFiles(Path.Combine(root, "tests"), "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(path => path.Contains("dmarc", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains("/bin/", StringComparison.Ordinal) && !path.Contains("/obj/", StringComparison.Ordinal))
            .Where(path => TextExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Where(path => !path.EndsWith("/" + Path.GetFileName(thisFile), StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    // thisFile = <repo>/tests/Jobbliggaren.Architecture.Tests/DmarcFixtureAddressGuardTests.cs
    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}

/// <summary>
/// Finds the IPv4 and IPv6 addresses written in a text, wherever they stand: a string, a comment, XML. A dotted quad
/// is four decimal octets that no further digit group continues; an IPv6 address is a run of hex groups and colons the
/// base library parses, its zone dropped.
/// </summary>
internal static partial class FixtureAddressScan
{
    public static List<IPAddress> Addresses(string text)
    {
        var addresses = new List<IPAddress>();

        foreach (Match match in DottedQuad().Matches(text))
        {
            var octets = match.Value.Split('.').Select(octet => int.Parse(octet, CultureInfo.InvariantCulture)).ToArray();
            if (octets.All(octet => octet <= 255))
                addresses.Add(new IPAddress(octets.Select(octet => (byte)octet).ToArray()));
        }

        foreach (Match match in ColonGroups().Matches(text))
        {
            if (IPAddress.TryParse(match.Value, out var address) && address.AddressFamily == AddressFamily.InterNetworkV6)
                addresses.Add(address);
        }

        return addresses;
    }

    public static bool IsDocumentationAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        var bytes = address.GetAddressBytes();
        return address.AddressFamily == AddressFamily.InterNetwork
            ? bytes is [192, 0, 2, _] or [198, 51, 100, _] or [203, 0, 113, _]
            : bytes is [0x20, 0x01, 0x0D, 0xB8, ..];
    }

    [GeneratedRegex(@"(?<![\w.])\d{1,3}(?:\.\d{1,3}){3}(?!\w|\.\d)", RegexOptions.CultureInvariant)]
    private static partial Regex DottedQuad();

    [GeneratedRegex(@"(?<![\w:])[0-9A-Fa-f.]*:[0-9A-Fa-f:.]*:[0-9A-Fa-f.]*(?![\w:])", RegexOptions.CultureInvariant)]
    private static partial Regex ColonGroups();
}
