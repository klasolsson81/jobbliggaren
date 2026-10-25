using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Jobbliggaren.Application.UnitTests.Email.Dmarc;

/// <summary>
/// Synthetic DMARC aggregate reports, built from the RFC 7489 Appendix C and RFC 9990 Appendix A trees. What follows a
/// real reporter is only the outer shape the public IETF dmarc-report archive showed on 2026-10-24: Google sends one
/// zip entry and ends its day on the last second, Microsoft and Yahoo send gzip, Microsoft ends its day exclusively.
/// Every value inside the XML is invented, every address is from the documentation ranges, and nothing here is
/// copied from a report or from the dmarc@ mailbox. The elements the decoder must never keep carry a "canary" value.
/// </summary>
internal sealed record SyntheticReport
{
    public const string Rfc7489Namespace = "http://dmarc.org/dmarc-xml/0.1";
    public const string Rfc9990Namespace = "urn:ietf:params:xml:ns:dmarc-2.0";

    /// <summary>2026-10-23 00:00:00Z.</summary>
    public const long DayBegin = 1792713600;

    /// <summary>The root's namespace; <see langword="null"/> is the namespace-less form most reporters send.</summary>
    public string? Namespace { get; init; }

    public string Reporter { get; init; } = "reporter.example";

    public string ReportId { get; init; } = "synthetic-report-0001";

    public string Begin { get; init; } = DayBegin.ToString(CultureInfo.InvariantCulture);

    public string End { get; init; } = (DayBegin + 86399).ToString(CultureInfo.InvariantCulture);

    public string PolicyDomain { get; init; } = "jobbliggaren.se";

    public IReadOnlyList<SyntheticRecord> Records { get; init; } = [new()];

    /// <summary>The RFC 9990 additions: no version, a generator, discovery_method, np, testing and extensions.</summary>
    public bool Rfc9990Elements { get; init; }

    /// <summary>Written around every kept value, which the decoder trims.</summary>
    public string Padding { get; init; } = "";

    public static SyntheticReport GoogleShaped() => new()
    {
        Reporter = "google.com",
        ReportId = "17927136000000000001",
        Records =
        [
            new("192.0.2.25", 12, "jobbliggaren.se", "pass", "pass"),
            new("2001:db8:4860::7", 3, "jobbliggaren.se", "pass", "fail"),
            new("198.51.100.40", 1, "mail.jobbliggaren.se", "fail", "fail"),
        ],
    };

    public static SyntheticReport MicrosoftShaped() => new()
    {
        Reporter = "protection.outlook.com",
        ReportId = "0123456789abcdef0123456789abcdef",
        End = (DayBegin + 86400).ToString(CultureInfo.InvariantCulture),
        Records =
        [
            new("203.0.113.5", 7, "jobbliggaren.se", "pass", "pass"),
            new("203.0.113.6", 2, "jobbliggaren.se", "fail", "pass"),
        ],
    };

    public static SyntheticReport YahooShaped() => new()
    {
        Reporter = "Yahoo",
        ReportId = "1792713600.0123",
        Records = [new("198.51.100.77", 4, "jobbliggaren.se", "pass", "fail")],
    };

    public static SyntheticReport Rfc9990Shaped() => new()
    {
        Namespace = Rfc9990Namespace,
        Rfc9990Elements = true,
        Reporter = "receiver.example",
        ReportId = "<rfc9990.0001@receiver.example>",
        Records =
        [
            new("2001:db8::25", 9, "jobbliggaren.se", "pass", "pass"),
            new("192.0.2.200", 1, "jobbliggaren.se", "fail", "fail"),
        ],
    };

    /// <summary>The RFC 9990 §3.5.2 filename: receiver, policy domain, begin and end.</summary>
    public string ZipEntryName() => $"{Reporter}!{PolicyDomain}!{Begin}!{End}.xml";

    public byte[] ToUtf8() => Encoding.UTF8.GetBytes(ToXml());

    public string ToXml()
    {
        var xml = new StringBuilder();
        xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        xml.Append(Namespace is null ? "<feedback>\n" : $"<feedback xmlns=\"{Namespace}\">\n");
        if (!Rfc9990Elements)
            xml.Append("  <version>1.0</version>\n");

        xml.Append("  <report_metadata>\n");
        xml.Append(CultureInfo.InvariantCulture, $"    <org_name>{Kept(Reporter)}</org_name>\n");
        xml.Append("    <email>canary-email@reporter.example</email>\n");
        xml.Append("    <extra_contact_info>canary-extra-contact-info</extra_contact_info>\n");
        xml.Append(CultureInfo.InvariantCulture, $"    <report_id>{Kept(ReportId)}</report_id>\n");
        xml.Append("    <date_range>\n");
        xml.Append(CultureInfo.InvariantCulture, $"      <begin>{Kept(Begin)}</begin>\n");
        xml.Append(CultureInfo.InvariantCulture, $"      <end>{Kept(End)}</end>\n");
        xml.Append("    </date_range>\n");
        xml.Append("    <error>canary-error</error>\n");
        if (Rfc9990Elements)
            xml.Append("    <generator>canary-generator 1.0</generator>\n");
        xml.Append("  </report_metadata>\n");

        xml.Append("  <policy_published>\n");
        xml.Append(CultureInfo.InvariantCulture, $"    <domain>{Kept(PolicyDomain)}</domain>\n");
        if (Rfc9990Elements)
            xml.Append("    <discovery_method>treewalk</discovery_method>\n");
        xml.Append("    <adkim>r</adkim>\n");
        xml.Append("    <aspf>r</aspf>\n");
        xml.Append("    <p>none</p>\n");
        xml.Append("    <sp>none</sp>\n");
        if (Rfc9990Elements)
            xml.Append("    <np>none</np>\n    <testing>n</testing>\n");
        else
            xml.Append("    <pct>100</pct>\n");
        xml.Append("    <fo>1</fo>\n");
        xml.Append("  </policy_published>\n");

        if (Rfc9990Elements)
        {
            xml.Append("  <extension>\n");
            xml.Append("    <ext:note xmlns:ext=\"urn:example:extension\">canary-extension</ext:note>\n");
            xml.Append("  </extension>\n");
        }

        foreach (var record in Records)
            AppendRecord(xml, record);

        xml.Append("</feedback>\n");
        return xml.ToString();
    }

    private void AppendRecord(StringBuilder xml, SyntheticRecord record)
    {
        xml.Append("  <record>\n");
        xml.Append("    <row>\n");
        xml.Append(CultureInfo.InvariantCulture, $"      <source_ip>{Kept(record.SourceIp)}</source_ip>\n");
        var count = record.Count.ToString(CultureInfo.InvariantCulture);
        xml.Append(CultureInfo.InvariantCulture, $"      <count>{Kept(count)}</count>\n");
        xml.Append("      <policy_evaluated>\n");
        xml.Append("        <disposition>none</disposition>\n");
        xml.Append(CultureInfo.InvariantCulture, $"        <dkim>{Kept(record.Dkim)}</dkim>\n");
        xml.Append(CultureInfo.InvariantCulture, $"        <spf>{Kept(record.Spf)}</spf>\n");
        xml.Append("        <reason>\n          <type>local_policy</type>\n");
        xml.Append("          <comment>canary-comment</comment>\n        </reason>\n");
        xml.Append("      </policy_evaluated>\n");
        xml.Append("    </row>\n");
        xml.Append("    <identifiers>\n");
        xml.Append("      <envelope_to>canary-envelope-to.example</envelope_to>\n");
        xml.Append("      <envelope_from>canary-envelope-from.example</envelope_from>\n");
        xml.Append(CultureInfo.InvariantCulture, $"      <header_from>{Kept(record.HeaderFrom)}</header_from>\n");
        xml.Append("    </identifiers>\n");
        xml.Append("    <auth_results>\n");
        xml.Append("      <dkim>\n        <domain>canary-dkim.example</domain>\n        <selector>canary-selector</selector>\n");
        xml.Append("        <result>pass</result>\n        <human_result>canary-human-result</human_result>\n      </dkim>\n");
        xml.Append("      <spf>\n        <domain>canary-spf.example</domain>\n        <scope>mfrom</scope>\n");
        xml.Append("        <result>pass</result>\n      </spf>\n");
        xml.Append("    </auth_results>\n");
        if (Rfc9990Elements)
        {
            xml.Append("    <extension>\n      <ext:record xmlns:ext=\"urn:example:extension\">\n");
            xml.Append("        <ext:source_ip>192.0.2.254</ext:source_ip>\n      </ext:record>\n    </extension>\n");
        }

        xml.Append("  </record>\n");
    }

    private string Kept(string value) => Padding + System.Security.SecurityElement.Escape(value) + Padding;
}

/// <summary>One record of a <see cref="SyntheticReport"/>: the fields the decoder keeps.</summary>
internal sealed record SyntheticRecord(
    string SourceIp = "192.0.2.1",
    long Count = 1,
    string HeaderFrom = "jobbliggaren.se",
    string Dkim = "pass",
    string Spf = "pass");

/// <summary>How the synthetic XML is packed, as a reporter would pack it.</summary>
internal static class SyntheticPacking
{
    public static byte[] Gzip(byte[] xml, DateTimeOffset? modified = null)
    {
        using var packed = new MemoryStream();
        using (var gzip = new GZipStream(packed, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(xml);

        var bytes = packed.ToArray();
        if (modified is { } time)
        {
            // RFC 1952 §2.3: MTIME is bytes 4–7, little-endian; GZipStream always writes zero there.
            BitConverter.GetBytes((uint)time.ToUnixTimeSeconds()).CopyTo(bytes, 4);
        }

        return bytes;
    }

    /// <summary>One gzip member per part, concatenated (RFC 1952 §2.2).</summary>
    public static byte[] GzipMembers(params byte[][] parts) => [.. parts.SelectMany(part => Gzip(part))];

    public static byte[] Zip(params (string Name, byte[] Content)[] entries) =>
        Zip(DateTimeOffset.FromUnixTimeSeconds(SyntheticReport.DayBegin), entries);

    public static byte[] Zip(DateTimeOffset modified, params (string Name, byte[] Content)[] entries)
    {
        using var packed = new MemoryStream();
        using (var archive = new ZipArchive(packed, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
                entry.LastWriteTime = modified;
                using var stream = entry.Open();
                stream.Write(content);
            }
        }

        return packed.ToArray();
    }

    /// <summary>
    /// The offset of the first central directory header (APPNOTE 4.3.12), where ZipArchive reads an entry's declared
    /// sizes and flags.
    /// </summary>
    public static int CentralDirectoryOffset(byte[] zip)
    {
        for (var i = zip.Length - 22; i >= 0; i--)
        {
            if (zip[i] == 0x50 && zip[i + 1] == 0x4B && zip[i + 2] == 0x01 && zip[i + 3] == 0x02)
                return i;
        }

        throw new InvalidOperationException("The archive has no central directory header.");
    }

    /// <summary>The single entry's declared uncompressed size rewritten, as a lying archive would declare it.</summary>
    public static byte[] WithDeclaredLength(byte[] zip, uint length)
    {
        var patched = (byte[])zip.Clone();
        BitConverter.GetBytes(length).CopyTo(patched, CentralDirectoryOffset(patched) + 24);
        return patched;
    }
}

internal static class SyntheticText
{
    /// <summary>
    /// Replaces the one occurrence of <paramref name="old"/>, so a test cannot pass on a fragment it never found.
    /// </summary>
    public static string ReplaceOnce(this string text, string old, string replacement)
    {
        var at = text.IndexOf(old, StringComparison.Ordinal);
        if (at < 0 || text.IndexOf(old, at + old.Length, StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException($"Expected exactly one '{old}'.");
        return string.Concat(text.AsSpan(0, at), replacement, text.AsSpan(at + old.Length));
    }

    /// <summary>The first <c>&lt;name&gt;…&lt;/name&gt;</c> element, children included.</summary>
    public static string FirstElement(this string xml, string name)
    {
        var start = xml.IndexOf($"<{name}>", StringComparison.Ordinal);
        var close = $"</{name}>";
        var end = start < 0 ? -1 : xml.IndexOf(close, start, StringComparison.Ordinal);
        if (end < 0)
            throw new InvalidOperationException($"No <{name}> element.");
        return xml[start..(end + close.Length)];
    }
}
