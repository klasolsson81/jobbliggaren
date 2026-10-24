using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Jobbliggaren.Application.Admin.Email.Dmarc;
using Jobbliggaren.Infrastructure.Email.Dmarc.Decoding;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Email.Dmarc;

// The decoder reads what the mailbox adapter hands it: one report part, transfer-decoded, still compressed as the
// message declared it. Every input below is synthetic (SyntheticDmarcReports.cs). The attack inputs are modelled on no
// observed message; their actor is any sender to the published rua=mailto:dmarc@ address, who controls every byte of
// the part.
public sealed class XmlDmarcReportDecoderTests
{
    private const long Cap = DmarcIngestionLimits.MaxDecompressedXmlBytes;

    private static readonly DateTimeOffset Received = DateTimeOffset.FromUnixTimeSeconds(SyntheticReport.DayBegin + 90_000);

    private readonly XmlDmarcReportDecoder _decoder = new();

    [Fact]
    public void Decode_AGoogleShapedZip_KeepsExactlyTheReportedFields()
    {
        var report = SyntheticReport.GoogleShaped();
        var zip = SyntheticPacking.Zip((report.ZipEntryName(), report.ToUtf8()));

        var candidate = Decoded(zip, DmarcReportPartKind.Zip);

        candidate.Reporter.ShouldBe("google.com");
        candidate.ReportId.ShouldBe("17927136000000000001");
        candidate.PolicyDomain.ShouldBe("jobbliggaren.se");
        candidate.BeginUnixSeconds.ShouldBe(SyntheticReport.DayBegin);
        candidate.EndUnixSeconds.ShouldBe(SyntheticReport.DayBegin + 86399);
        candidate.Records.ShouldBe(
        [
            new DmarcRecordCandidate("192.0.2.25", 12, "jobbliggaren.se", "pass", "pass"),
            new DmarcRecordCandidate("2001:db8:4860::7", 3, "jobbliggaren.se", "pass", "fail"),
            new DmarcRecordCandidate("198.51.100.40", 1, "mail.jobbliggaren.se", "fail", "fail"),
        ]);
    }

    [Fact]
    public void Decode_AMicrosoftShapedGzip_KeepsItsExclusiveDayEnd()
    {
        var candidate = Decoded(SyntheticPacking.Gzip(SyntheticReport.MicrosoftShaped().ToUtf8()), DmarcReportPartKind.Gzip);

        candidate.Reporter.ShouldBe("protection.outlook.com");
        candidate.EndUnixSeconds.ShouldBe(SyntheticReport.DayBegin + 86400);
        candidate.Records.Select(record => record.SourceIp).ShouldBe(["203.0.113.5", "203.0.113.6"]);
        candidate.Records.Select(record => record.Dkim).ShouldBe(["pass", "fail"]);
    }

    [Fact]
    public void Decode_AYahooShapedGzip_IsDecoded()
    {
        var candidate = Decoded(SyntheticPacking.Gzip(SyntheticReport.YahooShaped().ToUtf8()), DmarcReportPartKind.Gzip);

        candidate.Reporter.ShouldBe("Yahoo");
        candidate.Records.ShouldHaveSingleItem()
            .ShouldBe(new DmarcRecordCandidate("198.51.100.77", 4, "jobbliggaren.se", "pass", "fail"));
    }

    [Fact]
    public void Decode_TheRfc9990Form_IsReadByLocalName_AndItsNewElementsAreSkipped()
    {
        var candidate = Decoded(SyntheticReport.Rfc9990Shaped().ToUtf8(), DmarcReportPartKind.Xml);

        candidate.Reporter.ShouldBe("receiver.example");
        candidate.ReportId.ShouldBe("<rfc9990.0001@receiver.example>");
        candidate.Records.Select(record => record.SourceIp).ShouldBe(["2001:db8::25", "192.0.2.200"]);
    }

    [Fact]
    public void Decode_TheRfc7489FormWithItsNamespace_IsDecoded()
    {
        var report = SyntheticReport.GoogleShaped() with { Namespace = SyntheticReport.Rfc7489Namespace };

        Decoded(report.ToUtf8(), DmarcReportPartKind.Xml).Records.Count.ShouldBe(3);
    }

    [Fact]
    public void Decode_AGzipOfSeveralMembers_IsDecodedAsOneDocument()
    {
        var xml = SyntheticReport.GoogleShaped().ToUtf8();
        var gzip = SyntheticPacking.GzipMembers(xml[..100], xml[100..1000], xml[1000..]);

        var candidate = Decoded(gzip, DmarcReportPartKind.Gzip);

        candidate.Records.Count.ShouldBe(3);
        candidate.ContentSha256.ShouldBe(SHA256.HashData(xml));
    }

    [Fact]
    public void Decode_WhitespaceAroundAValue_IsTrimmed()
    {
        var report = SyntheticReport.GoogleShaped() with { Padding = " \r\n\t " };

        var candidate = Decoded(report.ToUtf8(), DmarcReportPartKind.Xml);

        candidate.Reporter.ShouldBe("google.com");
        candidate.ReportId.ShouldBe("17927136000000000001");
        candidate.PolicyDomain.ShouldBe("jobbliggaren.se");
        candidate.BeginUnixSeconds.ShouldBe(SyntheticReport.DayBegin);
        candidate.Records[0].ShouldBe(new DmarcRecordCandidate("192.0.2.25", 12, "jobbliggaren.se", "pass", "pass"));
    }

    [Fact]
    public void Decode_NoElementTheDecoderSkips_ReachesTheCandidate()
    {
        var candidate = Decoded(SyntheticReport.Rfc9990Shaped().ToUtf8(), DmarcReportPartKind.Xml);

        string[] kept =
        [
            candidate.Reporter, candidate.ReportId, candidate.PolicyDomain,
            .. candidate.Records.SelectMany(record => new[] { record.SourceIp, record.HeaderFrom, record.Dkim, record.Spf }),
        ];
        kept.ShouldAllBe(value => !value.Contains("canary", StringComparison.Ordinal));
    }

    [Fact]
    public void Decode_UnknownElementsAttributesCommentsAndInstructions_AreIgnored()
    {
        var xml = SyntheticReport.YahooShaped().ToXml()
            .ReplaceOnce(
                "<feedback>",
                "<feedback xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" lang=\"en\"><!-- c --><?pi x?>")
            .ReplaceOnce("<report_metadata>", "<report_metadata><unknown a=\"1\"><deeper><deepest/></deeper></unknown>")
            .ReplaceOnce("<policy_published>", "<policy_published><future_tag>y</future_tag>")
            .ReplaceOnce("<row>", "<row><future_row_field>1</future_row_field>")
            .ReplaceOnce("<org_name>Yahoo</org_name>", "<org_name lang=\"en\"><!-- c -->Yahoo</org_name>")
            .ReplaceOnce("</feedback>", "<trailing_unknown/></feedback>");

        var candidate = Decoded(Encoding.UTF8.GetBytes(xml), DmarcReportPartKind.Xml);

        candidate.Reporter.ShouldBe("Yahoo");
        candidate.Records.ShouldHaveSingleItem().SourceIp.ShouldBe("198.51.100.77");
    }

    [Fact]
    public void Decode_ACharacterDataSectionOrAnEntityReference_IsReadAsText()
    {
        var xml = SyntheticReport.YahooShaped().ToXml()
            .ReplaceOnce("<org_name>Yahoo</org_name>", "<org_name><![CDATA[Yahoo]]></org_name>")
            .ReplaceOnce("<report_id>1792713600.0123</report_id>", "<report_id>a&amp;b&#x41;</report_id>");

        var candidate = Decoded(Encoding.UTF8.GetBytes(xml), DmarcReportPartKind.Xml);

        candidate.Reporter.ShouldBe("Yahoo");
        candidate.ReportId.ShouldBe("a&bA");
    }

    [Fact]
    public void Decode_AByteOrderMark_IsAccepted_AndHashedWithTheDocument()
    {
        byte[] xml = [.. Encoding.UTF8.GetPreamble(), .. SyntheticReport.YahooShaped().ToUtf8()];

        Decoded(xml, DmarcReportPartKind.Xml).ContentSha256.ShouldBe(SHA256.HashData(xml));
    }

    [Fact]
    public void Decode_ARecordInAnotherNamespace_IsNotCounted()
    {
        var xml = SyntheticReport.YahooShaped().ToXml().ReplaceOnce(
            "</feedback>",
            "<x:record xmlns:x=\"urn:example:other\"><x:row><x:source_ip>192.0.2.99</x:source_ip></x:row></x:record></feedback>");

        Decoded(Encoding.UTF8.GetBytes(xml), DmarcReportPartKind.Xml).Records.ShouldHaveSingleItem()
            .SourceIp.ShouldBe("198.51.100.77");
    }

    [Fact]
    public void Decode_ARecordInsideAnExtension_IsNotCounted()
    {
        // Rfc9990Shaped puts an <ext:record> with its own source_ip inside every record's extension block.
        Decoded(SyntheticReport.Rfc9990Shaped().ToUtf8(), DmarcReportPartKind.Xml).Records
            .ShouldAllBe(record => record.SourceIp != "192.0.2.254");
    }

    [Fact]
    public void Decode_AReportWithNoRecord_IsDecoded_SoAcceptCanNameIt()
    {
        var candidate = Decoded((SyntheticReport.YahooShaped() with { Records = [] }).ToUtf8(), DmarcReportPartKind.Xml);

        candidate.Records.ShouldBeEmpty();
        DmarcAggregateReport.Accept(candidate, Received).ShouldBe(new DmarcAcceptance.Rejected(DmarcRejection.NoRecords));
    }

    [Fact]
    public void Decode_MoreRecordsThanTheCap_StopsAtOnePast_SoAcceptRefusesThem()
    {
        var records = Enumerable.Range(0, DmarcIngestionLimits.MaxRecordsPerReport + 1)
            .Select(i => new SyntheticRecord($"192.0.2.{i % 256}", i + 1))
            .ToList();
        var xml = (SyntheticReport.YahooShaped() with { Records = records }).ToXml()
            .ReplaceOnce("</feedback>", "<record><row><count>not a number</count></row></record></feedback>");

        var candidate = Decoded(SyntheticPacking.Gzip(Encoding.UTF8.GetBytes(xml)), DmarcReportPartKind.Gzip);

        candidate.Records.Count.ShouldBe(DmarcIngestionLimits.MaxRecordsPerReport + 1);
        DmarcAggregateReport.Accept(candidate, Received)
            .ShouldBe(new DmarcAcceptance.Rejected(DmarcRejection.TooManyRecords));
    }

    [Fact]
    public void Decode_ANegativeCount_IsReadAsWritten_SoAcceptRefusesIt()
    {
        var xml = SyntheticReport.YahooShaped().ToXml().ReplaceOnce("<count>4</count>", "<count>-1</count>");

        var candidate = Decoded(Encoding.UTF8.GetBytes(xml), DmarcReportPartKind.Xml);

        candidate.Records[0].Count.ShouldBe(-1);
        DmarcAggregateReport.Accept(candidate, Received)
            .ShouldBe(new DmarcAcceptance.Rejected(DmarcRejection.CountOutOfRange));
    }

    [Fact]
    public void Decode_TheDigest_IsTheSha256OfTheDecompressedXml_WhateverPackedIt()
    {
        var xml = SyntheticReport.GoogleShaped().ToUtf8();
        var zipped = SyntheticPacking.Zip(
            DateTimeOffset.FromUnixTimeSeconds(SyntheticReport.DayBegin + 90_000), ("report.xml", xml));
        var zippedLater = SyntheticPacking.Zip(
            DateTimeOffset.FromUnixTimeSeconds(SyntheticReport.DayBegin + 900_000), ("report.xml", xml));
        var gzipped = SyntheticPacking.Gzip(xml, DateTimeOffset.FromUnixTimeSeconds(SyntheticReport.DayBegin + 1_000));

        zipped.ShouldNotBe(zippedLater);

        var expected = SHA256.HashData(xml);
        Decoded(xml, DmarcReportPartKind.Xml).ContentSha256.ShouldBe(expected);
        Decoded(zipped, DmarcReportPartKind.Zip).ContentSha256.ShouldBe(expected);
        Decoded(zippedLater, DmarcReportPartKind.Zip).ContentSha256.ShouldBe(expected);
        Decoded(gzipped, DmarcReportPartKind.Gzip).ContentSha256.ShouldBe(expected);
    }

    [Theory]
    [InlineData("REPORT.XML")]
    [InlineData("reports/google.com!jobbliggaren.se!1792713600!1792799999.xml")]
    public void Decode_AZipEntryNamedXmlInAnyCase_IsRead(string name)
    {
        Decoded(SyntheticPacking.Zip((name, SyntheticReport.YahooShaped().ToUtf8())), DmarcReportPartKind.Zip)
            .Reporter.ShouldBe("Yahoo");
    }

    [Fact]
    public void Decode_AZipWithNoEntry_IsMalformed()
    {
        ShouldBeMalformed(SyntheticPacking.Zip(), DmarcReportPartKind.Zip);
    }

    [Fact]
    public void Decode_AZipWithTwoEntries_IsMalformed()
    {
        var xml = SyntheticReport.YahooShaped().ToUtf8();

        ShouldBeMalformed(SyntheticPacking.Zip(("a.xml", xml), ("b.xml", xml)), DmarcReportPartKind.Zip);
        ShouldBeMalformed(SyntheticPacking.Zip(("reports/", []), ("reports/a.xml", xml)), DmarcReportPartKind.Zip);
    }

    [Theory]
    [InlineData("report.txt")]
    [InlineData("report.xml.txt")]
    [InlineData("report.xml/")]
    [InlineData("report")]
    public void Decode_AZipEntryNotNamedXml_IsMalformed(string name)
    {
        ShouldBeMalformed(SyntheticPacking.Zip((name, SyntheticReport.YahooShaped().ToUtf8())), DmarcReportPartKind.Zip);
    }

    [Fact]
    public void Decode_ANestedArchive_IsMalformed()
    {
        var xml = SyntheticReport.YahooShaped().ToUtf8();
        var innerZip = SyntheticPacking.Zip(("report.xml", xml));
        var innerGzip = SyntheticPacking.Gzip(xml);

        ShouldBeMalformed(SyntheticPacking.Zip(("report.xml.gz", innerGzip)), DmarcReportPartKind.Zip);
        ShouldBeMalformed(SyntheticPacking.Zip(("report.zip", innerZip)), DmarcReportPartKind.Zip);
        ShouldBeMalformed(SyntheticPacking.Zip(("report.xml", innerGzip)), DmarcReportPartKind.Zip);
        ShouldBeMalformed(SyntheticPacking.Zip(("report.xml", innerZip)), DmarcReportPartKind.Zip);
        ShouldBeMalformed(SyntheticPacking.Gzip(innerGzip), DmarcReportPartKind.Gzip);
    }

    [Fact]
    public void Decode_AZipEntryDeclaringMoreThanTheCap_IsMalformed_WithoutInflatingIt()
    {
        var zip = SyntheticPacking.Zip(("report.xml", SyntheticReport.YahooShaped().ToUtf8()));

        Decoded(SyntheticPacking.WithDeclaredLength(zip, (uint)Cap), DmarcReportPartKind.Zip).Reporter.ShouldBe("Yahoo");
        ShouldBeMalformed(SyntheticPacking.WithDeclaredLength(zip, (uint)Cap + 1), DmarcReportPartKind.Zip);
    }

    [Fact]
    public void ZipArchive_AnEntryInflatingPastItsDeclaredLength_IsCutAtTheDeclaredLength()
    {
        // The base library's own behaviour, measured on .NET 10: the inflater stops at the central directory's
        // declared size and reports no error. Together with the declared-Length check it bounds what an entry inflates.
        var zip = SyntheticPacking.WithDeclaredLength(SyntheticPacking.Zip(("report.xml", Padded(Cap + 1024))), 4096);

        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        using var entry = archive.Entries.ShouldHaveSingleItem().Open();
        using var inflated = new MemoryStream();
        entry.CopyTo(inflated);

        inflated.Length.ShouldBe(4096);
    }

    [Fact]
    public void Decode_AZipEntryInflatingPastItsDeclaredLength_IsMalformed()
    {
        var lying = SyntheticPacking.WithDeclaredLength(SyntheticPacking.Zip(("report.xml", Padded(Cap + 1024))), 4096);

        lying.Length.ShouldBeLessThan(DmarcIngestionLimits.MaxReportPartBytes);
        ShouldBeMalformed(lying, DmarcReportPartKind.Zip);
    }

    [Fact]
    public void Decode_AnEncryptedZipEntry_IsMalformed()
    {
        var zip = SyntheticPacking.Zip(("report.xml", SyntheticReport.YahooShaped().ToUtf8()));
        var directory = SyntheticPacking.CentralDirectoryOffset(zip);
        zip[directory + 8] |= 0x01;
        zip[6] |= 0x01;

        ShouldBeMalformed(zip, DmarcReportPartKind.Zip);
    }

    [Fact]
    public void Decode_AZipEntryInACompressionMethodTheBaseLibraryLacks_IsMalformed()
    {
        var zip = SyntheticPacking.Zip(("report.xml", SyntheticReport.YahooShaped().ToUtf8()));
        var directory = SyntheticPacking.CentralDirectoryOffset(zip);
        zip[directory + 10] = 12;
        zip[8] = 12;

        ShouldBeMalformed(zip, DmarcReportPartKind.Zip);
    }

    [Fact]
    public void Decode_XmlExactlyAtTheCap_IsDecoded_AndOneBytePastItIsMalformed()
    {
        Decoded(SyntheticPacking.Gzip(Padded(Cap)), DmarcReportPartKind.Gzip).Reporter.ShouldBe("Yahoo");
        Decoded(SyntheticPacking.Zip(("report.xml", Padded(Cap))), DmarcReportPartKind.Zip).Reporter.ShouldBe("Yahoo");

        ShouldBeMalformed(SyntheticPacking.Gzip(Padded(Cap + 1)), DmarcReportPartKind.Gzip);
    }

    [Fact]
    public void Decode_AGzipBomb_IsMalformed()
    {
        var bomb = SyntheticPacking.Gzip(Padded(Cap + 1024 * 1024));

        bomb.Length.ShouldBeLessThan(DmarcIngestionLimits.MaxReportPartBytes);
        ShouldBeMalformed(bomb, DmarcReportPartKind.Gzip);
    }

    [Fact]
    public void Decode_GzipMembersTogetherPastTheCap_AreMalformed_ThoughEachIsUnderIt()
    {
        var xml = Padded(10L * 1024 * 1024);
        var half = xml.Length / 2;

        ShouldBeMalformed(SyntheticPacking.GzipMembers(xml[..half], xml[half..]), DmarcReportPartKind.Gzip);
    }

    [Fact]
    public void Decode_AGzipCutInsideItsCompressedData_IsMalformed()
    {
        // GZipStream reports no error for a cut stream (measured on .NET 10); what it inflated is an unfinished
        // document, which the reader refuses.
        var gzip = SyntheticPacking.Gzip(SyntheticReport.GoogleShaped().ToUtf8());

        ShouldBeMalformed(gzip[..(gzip.Length / 2)], DmarcReportPartKind.Gzip);
    }

    [Fact]
    public void Decode_AGzipWhoseChecksumDoesNotMatch_IsMalformed()
    {
        var gzip = SyntheticPacking.Gzip(SyntheticReport.YahooShaped().ToUtf8());
        gzip[^8] ^= 0xFF;

        ShouldBeMalformed(gzip, DmarcReportPartKind.Gzip);
    }

    [Theory]
    [InlineData("<!DOCTYPE feedback [ <!ENTITY e \"Yahoo\"> ]>", "<org_name>&e;</org_name>")]
    [InlineData("<!DOCTYPE feedback [ <!ELEMENT feedback ANY> ]>", "<org_name>Yahoo</org_name>")]
    [InlineData("<!DOCTYPE feedback SYSTEM \"http://dtd.invalid/report.dtd\">", "<org_name>Yahoo</org_name>")]
    [InlineData("<!DOCTYPE feedback [ <!ENTITY e SYSTEM \"file:///etc/hostname\"> ]>", "<org_name>&e;</org_name>")]
    public void Decode_ADocumentTypeDeclaration_IsMalformed(string doctype, string orgName)
    {
        var xml = SyntheticReport.YahooShaped().ToXml()
            .ReplaceOnce("<feedback>", doctype + "<feedback>")
            .ReplaceOnce("<org_name>Yahoo</org_name>", orgName);

        ShouldBeMalformed(Encoding.UTF8.GetBytes(xml), DmarcReportPartKind.Xml);
        ShouldBeMalformed(SyntheticPacking.Gzip(Encoding.UTF8.GetBytes(xml)), DmarcReportPartKind.Gzip);
    }

    [Fact]
    public void Decode_BillionLaughs_IsMalformed()
    {
        var entities = new StringBuilder("<!DOCTYPE feedback [ <!ENTITY l0 \"ha\">");
        for (var level = 1; level <= 10; level++)
        {
            var tenOfThePrevious = string.Concat(Enumerable.Repeat($"&l{level - 1};", 10));
            entities.Append(CultureInfo.InvariantCulture, $"<!ENTITY l{level} \"{tenOfThePrevious}\">");
        }
        entities.Append(" ]>");
        var xml = SyntheticReport.YahooShaped().ToXml()
            .ReplaceOnce("<feedback>", entities + "<feedback>")
            .ReplaceOnce("<org_name>Yahoo</org_name>", "<org_name>&l10;</org_name>");

        ShouldBeMalformed(SyntheticPacking.Gzip(Encoding.UTF8.GetBytes(xml)), DmarcReportPartKind.Gzip);
    }

    [Theory]
    [InlineData("report_metadata")]
    [InlineData("org_name")]
    [InlineData("report_id")]
    [InlineData("date_range")]
    [InlineData("begin")]
    [InlineData("end")]
    [InlineData("policy_published")]
    [InlineData("domain")]
    [InlineData("row")]
    [InlineData("source_ip")]
    [InlineData("count")]
    [InlineData("policy_evaluated")]
    [InlineData("dkim")]
    [InlineData("spf")]
    [InlineData("identifiers")]
    [InlineData("header_from")]
    public void Decode_ARequiredElementMissingOrRepeated_IsMalformed(string name)
    {
        var xml = SyntheticReport.YahooShaped().ToXml();
        var element = xml.FirstElement(name);

        ShouldBeMalformed(Encoding.UTF8.GetBytes(xml.ReplaceOnce(element, "")), DmarcReportPartKind.Xml);
        ShouldBeMalformed(Encoding.UTF8.GetBytes(xml.ReplaceOnce(element, element + element)), DmarcReportPartKind.Xml);
        ShouldBeMalformed(Encoding.UTF8.GetBytes(xml.ReplaceOnce(element, element + $"<{name}/>")), DmarcReportPartKind.Xml);
    }

    [Theory]
    [InlineData("<begin>1792713600</begin>", "<begin>yesterday</begin>")]
    [InlineData("<begin>1792713600</begin>", "<begin>1792713600.5</begin>")]
    [InlineData("<begin>1792713600</begin>", "<begin>99999999999999999999</begin>")]
    [InlineData("<end>1792799999</end>", "<end/>")]
    [InlineData("<end>1792799999</end>", "<end>1 792 799 999</end>")]
    [InlineData("<count>4</count>", "<count>1.0</count>")]
    [InlineData("<count>4</count>", "<count>0x4</count>")]
    [InlineData("<count>4</count>", "<count>٤</count>")]
    [InlineData("<count>4</count>", "<count></count>")]
    public void Decode_ANumberThatIsNotAnInteger_IsMalformed(string valid, string invalid)
    {
        var xml = SyntheticReport.YahooShaped().ToXml().ReplaceOnce(valid, invalid);

        ShouldBeMalformed(Encoding.UTF8.GetBytes(xml), DmarcReportPartKind.Xml);
    }

    [Fact]
    public void Decode_ALeafWithElementContent_IsMalformed()
    {
        var xml = SyntheticReport.YahooShaped().ToXml()
            .ReplaceOnce("<org_name>Yahoo</org_name>", "<org_name><b>Yahoo</b></org_name>");

        ShouldBeMalformed(Encoding.UTF8.GetBytes(xml), DmarcReportPartKind.Xml);
    }

    [Theory]
    [InlineData("<report><feedback/></report>")]
    [InlineData("<feedback xmlns=\"urn:example:other\"><report_metadata/></feedback>")]
    [InlineData("<feedback/><feedback/>")]
    [InlineData("<feedback></feedback>")]
    public void Decode_ADocumentThatIsNotOneReport_IsMalformed(string xml)
    {
        ShouldBeMalformed(Encoding.UTF8.GetBytes(xml), DmarcReportPartKind.Xml);
    }

    [Theory]
    [InlineData("</feedback>\n<feedback/>")]
    [InlineData("</feedback>\ntrailing text")]
    public void Decode_ContentAfterTheReport_IsMalformed(string ending)
    {
        var xml = SyntheticReport.YahooShaped().ToXml().ReplaceOnce("</feedback>", ending);

        ShouldBeMalformed(Encoding.UTF8.GetBytes(xml), DmarcReportPartKind.Xml);
    }

    [Fact]
    public void Decode_NestingAtTheDepthBound_IsDecoded_AndOneLevelDeeperIsMalformed()
    {
        // feedback is depth 0 and <extension> depth 1, so the innermost <x> sits at depth levels + 1.
        Decoded(Nested(DmarcIngestionLimits.MaxXmlElementDepth - 1), DmarcReportPartKind.Gzip).Reporter.ShouldBe("Yahoo");
        ShouldBeMalformed(Nested(DmarcIngestionLimits.MaxXmlElementDepth), DmarcReportPartKind.Gzip);
    }

    [Fact]
    public void Decode_NestingAsDeepAsTheByteCapAllows_IsMalformed_WithinABoundedAllocation()
    {
        var xml = SyntheticReport.YahooShaped().ToXml();
        var levels = (int)((Cap - Encoding.UTF8.GetByteCount(xml) - "<extension>".Length) / 3);
        var deep = Encoding.UTF8.GetBytes(xml.ReplaceOnce(
            "<report_metadata>", "<report_metadata><extension>" + string.Concat(Enumerable.Repeat("<x>", levels))));
        deep.LongLength.ShouldBeLessThanOrEqualTo(Cap);
        var gzip = SyntheticPacking.Gzip(deep);

        var before = GC.GetAllocatedBytesForCurrentThread();
        ShouldBeMalformed(gzip, DmarcReportPartKind.Gzip);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Without the depth bound the reader keeps a node per open tag: about 500 MB for 8 MiB of them (2026-10-24).
        allocated.ShouldBeLessThan(8 * Cap);
    }

    [Theory]
    [InlineData(DmarcReportPartKind.Xml)]
    [InlineData(DmarcReportPartKind.Gzip)]
    [InlineData(DmarcReportPartKind.Zip)]
    public void Decode_SomethingThatIsNotAReportAtAll_IsMalformed(DmarcReportPartKind kind)
    {
        ShouldBeMalformed([], kind);
        ShouldBeMalformed(Encoding.UTF8.GetBytes("This is not XML, not gzip and not zip."), kind);
        ShouldBeMalformed([0x00, 0xFF, 0x1F, 0x8B, 0x50, 0x4B, 0x03, 0x04, 0xC3, 0x28], kind);
    }

    [Fact]
    public void Decode_XmlInAnEncodingTheReaderRefuses_IsMalformed()
    {
        var undeclared = SyntheticReport.YahooShaped().ToXml().ReplaceOnce("encoding=\"UTF-8\"", "encoding=\"x-unknown\"");
        byte[] invalidUtf8 = [.. Encoding.UTF8.GetBytes("<feedback><report_metadata><org_name>"), 0xC3, 0x28];

        ShouldBeMalformed(Encoding.UTF8.GetBytes(undeclared), DmarcReportPartKind.Xml);
        ShouldBeMalformed(invalidUtf8, DmarcReportPartKind.Xml);
    }

    [Fact]
    public void Decode_APartLargerThanTheFetchCap_IsMalformed()
    {
        // Declared unreachable (AGENTS.md §5 Tests): the mailbox adapter returns Skipped(TooLarge) for a part over
        // MaxReportPartBytes and never hands it on. Asserted only to degrade safely.
        var xml = SyntheticReport.YahooShaped().ToXml()
            .ReplaceOnce("</feedback>", "<pad>" + new string('a', DmarcIngestionLimits.MaxReportPartBytes) + "</pad></feedback>");

        ShouldBeMalformed(Encoding.UTF8.GetBytes(xml), DmarcReportPartKind.Xml);
    }

    [Theory]
    [InlineData(DmarcReportPartKind.Xml)]
    [InlineData(DmarcReportPartKind.Gzip)]
    [InlineData(DmarcReportPartKind.Zip)]
    public void Decode_EveryCutAndEverySingleByteChangeOfAReport_EndsInAResult(DmarcReportPartKind kind)
    {
        var xml = SyntheticReport.YahooShaped().ToUtf8();
        var part = kind switch
        {
            DmarcReportPartKind.Gzip => SyntheticPacking.Gzip(xml),
            DmarcReportPartKind.Zip => SyntheticPacking.Zip(("report.xml", xml)),
            _ => xml,
        };

        for (var length = 0; length < part.Length; length++)
            Should.NotThrow(() => _decoder.Decode(part[..length], kind));

        for (var position = 0; position < part.Length; position++)
        {
            foreach (var value in (byte[])[0x00, 0x01, 0x7F, 0x80, 0xFF])
            {
                var changed = (byte[])part.Clone();
                changed[position] = value;
                Should.NotThrow(() => _decoder.Decode(changed, kind));
            }
        }
    }

    [Fact]
    public void Decode_AKindThisReleaseDoesNotKnow_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => _decoder.Decode([], (DmarcReportPartKind)99));
    }

    private DmarcReportCandidate Decoded(byte[] content, DmarcReportPartKind kind) =>
        _decoder.Decode(content, kind).ShouldBeOfType<DmarcDecoding.Decoded>().Candidate;

    private void ShouldBeMalformed(byte[] content, DmarcReportPartKind kind) =>
        _decoder.Decode(content, kind).ShouldBeOfType<DmarcDecoding.Malformed>();

    // A Yahoo-shaped report padded to exactly totalBytes of UTF-8 with an unread element, mostly three-byte characters
    // so a character count stays far below the byte count.
    private static byte[] Padded(long totalBytes)
    {
        var xml = SyntheticReport.YahooShaped().ToXml();
        var room = (int)(totalBytes - Encoding.UTF8.GetByteCount(xml) - "<pad></pad>".Length);
        var padding = new StringBuilder(room);
        padding.Append('€', room / 3).Append('a', room % 3);
        var padded = Encoding.UTF8.GetBytes(xml.ReplaceOnce("</feedback>", $"<pad>{padding}</pad></feedback>"));
        padded.LongLength.ShouldBe(totalBytes);
        return padded;
    }

    private static byte[] Nested(int levels)
    {
        var xml = SyntheticReport.YahooShaped().ToXml().ReplaceOnce(
            "</feedback>",
            "<extension>" + string.Concat(Enumerable.Repeat("<x>", levels)) + string.Concat(Enumerable.Repeat("</x>", levels))
            + "</extension></feedback>");
        return SyntheticPacking.Gzip(Encoding.UTF8.GetBytes(xml));
    }
}
