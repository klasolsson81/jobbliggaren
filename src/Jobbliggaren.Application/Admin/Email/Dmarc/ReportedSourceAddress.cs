using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;

namespace Jobbliggaren.Application.Admin.Email.Dmarc;

/// <summary>
/// #1981 PR 4a (ADR 0184) — the sending server's address in one report record, as the reporter wrote it. It is
/// personal data when the server is a person's own (C-582/14 <i>Breyer</i>), so it is never logged.
/// <para>
/// <see cref="IPAddress.TryParse(string?, out IPAddress?)"/> alone accepts the legacy inet_addr forms ("65536",
/// "20.2") and scoped IPv6, which would store a source the reporter never named. An IPv4 address is therefore exactly
/// four dotted decimal octets without leading zeros (RFC 3986 IPv4address, which RFC 9990 names), also as the tail of
/// an IPv6 address; an IPv6 address is hex groups, colons and that tail only, with no scope, brackets or port; and an
/// IPv4-mapped IPv6 address becomes the IPv4 address, so one server is one source.
/// </para>
/// </summary>
public sealed record ReportedSourceAddress
{
    private ReportedSourceAddress(IPAddress value) => Value = value;

    public IPAddress Value { get; }

    public static bool TryParse(string? text, [NotNullWhen(true)] out ReportedSourceAddress? address)
    {
        address = null;
        if (string.IsNullOrEmpty(text))
            return false;

        if (text.Contains(':', StringComparison.Ordinal))
        {
            if (!IsIPv6Text(text)
                || !IPAddress.TryParse(text, out var v6)
                || v6.AddressFamily != AddressFamily.InterNetworkV6
                || v6.ScopeId != 0)
                return false;

            address = new ReportedSourceAddress(v6.IsIPv4MappedToIPv6 ? v6.MapToIPv4() : v6);
            return true;
        }

        if (!IsDottedQuad(text) || !IPAddress.TryParse(text, out var v4))
            return false;

        address = new ReportedSourceAddress(v4);
        return true;
    }

    /// <summary>The address as stored. A value read back from the database is trusted to have passed <see cref="TryParse"/>.</summary>
    public static ReportedSourceAddress FromStored(IPAddress value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.AddressFamily is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6))
            throw new ArgumentException("A stored source is an IPv4 or IPv6 address.", nameof(value));
        return new ReportedSourceAddress(value.IsIPv4MappedToIPv6 ? value.MapToIPv4() : value);
    }

    public override string ToString() => Value.ToString();

    private static bool IsIPv6Text(string text)
    {
        foreach (var c in text)
        {
            if (!char.IsAsciiHexDigit(c) && c is not (':' or '.'))
                return false;
        }

        var lastColon = text.LastIndexOf(':');
        return !text.Contains('.', StringComparison.Ordinal) || IsDottedQuad(text[(lastColon + 1)..]);
    }

    private static bool IsDottedQuad(string text)
    {
        var parts = text.Split('.');
        if (parts.Length != 4)
            return false;

        foreach (var part in parts)
        {
            if (part.Length is 0 or > 3 || (part.Length > 1 && part[0] == '0'))
                return false;
            foreach (var c in part)
            {
                if (c is < '0' or > '9')
                    return false;
            }
            if (int.Parse(part, System.Globalization.CultureInfo.InvariantCulture) > 255)
                return false;
        }

        return true;
    }
}
