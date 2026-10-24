using Jobbliggaren.Application.Admin.Email.Dmarc;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Email.Dmarc;

// Accept is the one place a report's bounds are decided. The candidates are the shapes the decoder produces from the
// report forms reporters send (RFC 7489 and RFC 9990), with addresses from the documentation ranges only.
public sealed class DmarcAggregateReportTests
{
    // 2026-10-23 00:00:00Z, the begin of a UTC-day report.
    private const long Begin = 1792713600;
    private static readonly DateTimeOffset Received = DateTimeOffset.FromUnixTimeSeconds(Begin + 86400 + 3 * 3600);

    [Fact]
    public void Accept_AGoogleShapedDay_IsAccepted_WithItsInclusiveEndMadeExclusive()
    {
        var report = Accepted(Candidate(end: Begin + 86399));

        report.BeginsAt.ShouldBe(DateTimeOffset.FromUnixTimeSeconds(Begin));
        report.EndsAt.ShouldBe(DateTimeOffset.FromUnixTimeSeconds(Begin + 86400));
        report.ReceivedAt.ShouldBe(Received);
    }

    [Fact]
    public void Accept_AMicrosoftShapedDay_KeepsItsExclusiveEnd()
    {
        var report = Accepted(Candidate(end: Begin + 86400));

        report.EndsAt.ShouldBe(DateTimeOffset.FromUnixTimeSeconds(Begin + 86400));
    }

    [Fact]
    public void Accept_KeepsOnlyTheAcceptedFields_InCanonicalForm()
    {
        var report = Accepted(Candidate(records:
        [
            Record(source: "::ffff:192.0.2.10", headerFrom: "JobbLiggaren.SE.", dkim: "PASS", spf: "fail", count: 3),
            Record(source: "2001:db8::5", headerFrom: "mail.jobbliggaren.se", dkim: "fail", spf: "softfail", count: 1),
        ]));

        report.Reporter.ShouldBe("google.com");
        report.ReportId.ShouldBe("1234567890");
        report.Records.Count.ShouldBe(2);
        report.Records[0].Source.ToString().ShouldBe("192.0.2.10");
        report.Records[0].HeaderFrom.ShouldBe("jobbliggaren.se");
        report.Records[0].Dkim.ShouldBe(DmarcAlignment.Pass);
        report.Records[0].Spf.ShouldBe(DmarcAlignment.Fail);
        report.Records[0].FailsDmarc.ShouldBeFalse();
        report.Records[1].Spf.ShouldBe(DmarcAlignment.Other);
        report.Records[1].FailsDmarc.ShouldBeTrue();
    }

    [Fact]
    public void Accept_CopiesTheDigest_SoTheCandidateCannotChangeTheReport()
    {
        var digest = Digest(0x11);
        var report = Accepted(Candidate() with { ContentSha256 = digest });

        digest[0] = 0x99;

        report.ContentSha256[0].ShouldBe((byte)0x11);
    }

    [Theory]
    [InlineData("example.org")]
    [InlineData("sub.jobbliggaren.se")]
    [InlineData("jobbliggaren.se.example")]
    [InlineData("")]
    public void Accept_AReportAboutAnotherDomain_IsRefused(string policyDomain)
    {
        Rejection(Candidate() with { PolicyDomain = policyDomain }).ShouldBe(DmarcRejection.PolicyDomain);
    }

    [Theory]
    [InlineData("JOBBLIGGAREN.SE")]
    [InlineData("jobbliggaren.se.")]
    public void Accept_ThePolicyDomainInAnotherCaseOrWithATrailingDot_IsAccepted(string policyDomain)
    {
        Accepted(Candidate() with { PolicyDomain = policyDomain }).Records.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("google‮com", DmarcRejection.ForbiddenCharacter)]
    [InlineData("google\u0007", DmarcRejection.ForbiddenCharacter)]
    [InlineData("google​com", DmarcRejection.ForbiddenCharacter)]
    [InlineData("google\ncom", DmarcRejection.ForbiddenCharacter)]
    [InlineData("   ", DmarcRejection.ForbiddenCharacter)]
    public void Accept_AReporterNameThatCanSpoofTheDisplay_IsRefused(string reporter, DmarcRejection expected)
    {
        Rejection(Candidate() with { Reporter = reporter }).ShouldBe(expected);
    }

    [Fact]
    public void Accept_AFieldLongerThanTheCap_IsRefused()
    {
        var longest = new string('a', DmarcIngestionLimits.MaxFieldLength);

        Accepted(Candidate() with { ReportId = longest }).ReportId.ShouldBe(longest);
        Rejection(Candidate() with { ReportId = longest + "a" }).ShouldBe(DmarcRejection.FieldTooLong);
    }

    [Theory]
    [InlineData("example.org")]
    [InlineData("jobbliggaren.se.example.org")]
    [InlineData("xjobbliggaren.se")]
    [InlineData("-a.jobbliggaren.se")]
    [InlineData("a_b.jobbliggaren.se")]
    [InlineData("jöbbliggaren.se")]
    [InlineData("jobbliggaren.se‮")]
    [InlineData("")]
    public void Accept_ARecordWhoseHeaderFromIsNotOurDomain_RefusesTheWholeReport(string headerFrom)
    {
        Rejection(Candidate(records: [Record(), Record(headerFrom: headerFrom)]))
            .ShouldBe(DmarcRejection.HeaderFromOutsideDomain);
    }

    [Theory]
    [InlineData("65536")]
    [InlineData("192.0.2")]
    [InlineData("2001:db8::1%1")]
    public void Accept_ARecordWithAMalformedSource_RefusesTheWholeReport(string source)
    {
        Rejection(Candidate(records: [Record(), Record(source: source)])).ShouldBe(DmarcRejection.MalformedSource);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(DmarcIngestionLimits.MaxRecordMessageCount + 1L)]
    public void Accept_ARecordCountOutsideTheBounds_RefusesTheWholeReport(long count)
    {
        Rejection(Candidate(records: [Record(), Record(count: count)])).ShouldBe(DmarcRejection.CountOutOfRange);
    }

    [Fact]
    public void Accept_TheLargestRecordCount_IsAccepted()
    {
        Accepted(Candidate(records: [Record(count: DmarcIngestionLimits.MaxRecordMessageCount)]))
            .Records[0].MessageCount.ShouldBe(DmarcIngestionLimits.MaxRecordMessageCount);
    }

    [Fact]
    public void Accept_NoRecords_IsRefused()
    {
        Rejection(Candidate(records: [])).ShouldBe(DmarcRejection.NoRecords);
    }

    [Fact]
    public void Accept_MoreRecordsThanTheCap_IsRefused()
    {
        var records = Enumerable.Repeat(Record(), DmarcIngestionLimits.MaxRecordsPerReport + 1).ToList();

        Rejection(Candidate(records: records)).ShouldBe(DmarcRejection.TooManyRecords);
    }

    [Theory]
    [InlineData(Begin, Begin)]
    [InlineData(Begin, Begin - 1)]
    [InlineData(-1, Begin)]
    [InlineData(Begin, Begin + 7 * 86400 + 1)]
    [InlineData(Begin + 86400 * 2, Begin + 86400 * 3)]
    public void Accept_AnImplausiblePeriod_IsRefused(long begin, long end)
    {
        Rejection(Candidate(begin: begin, end: end)).ShouldBe(DmarcRejection.ImplausibleRange);
    }

    [Fact]
    public void Accept_TheLongestPeriod_IsAccepted()
    {
        var received = DateTimeOffset.FromUnixTimeSeconds(Begin + 7 * 86400);

        DmarcAggregateReport.Accept(Candidate(end: Begin + 7 * 86400), received)
            .ShouldBeOfType<DmarcAcceptance.Accepted>().Report.EndsAt.ShouldBe(received);
    }

    [Fact]
    public void Accept_APeriodEndingADayAfterReceipt_IsAccepted_AndOneSecondLaterIsNot()
    {
        var received = DateTimeOffset.FromUnixTimeSeconds(Begin + 3600);
        var atBound = Candidate(end: Begin + 3600 + 86400);

        DmarcAggregateReport.Accept(atBound, received).ShouldBeOfType<DmarcAcceptance.Accepted>();
        DmarcAggregateReport.Accept(atBound with { EndUnixSeconds = Begin + 3600 + 86400 + 2 }, received)
            .ShouldBeOfType<DmarcAcceptance.Rejected>().Reason.ShouldBe(DmarcRejection.ImplausibleRange);
    }

    [Fact]
    public void Accept_ACandidateWithoutA32ByteDigest_IsAProgrammingError()
    {
        Should.Throw<ArgumentException>(() =>
            DmarcAggregateReport.Accept(Candidate() with { ContentSha256 = new byte[31] }, Received));
    }

    private static DmarcAggregateReport Accepted(DmarcReportCandidate candidate) =>
        DmarcAggregateReport.Accept(candidate, Received).ShouldBeOfType<DmarcAcceptance.Accepted>().Report;

    private static DmarcRejection Rejection(DmarcReportCandidate candidate) =>
        DmarcAggregateReport.Accept(candidate, Received).ShouldBeOfType<DmarcAcceptance.Rejected>().Reason;

    private static DmarcReportCandidate Candidate(
        long begin = Begin, long end = Begin + 86399, IReadOnlyList<DmarcRecordCandidate>? records = null) =>
        new("google.com", "1234567890", "jobbliggaren.se", begin, end, records ?? [Record()], Digest(0x42));

    private static DmarcRecordCandidate Record(
        string source = "192.0.2.1", long count = 1, string headerFrom = "jobbliggaren.se",
        string dkim = "pass", string spf = "pass") =>
        new(source, count, headerFrom, dkim, spf);

    private static byte[] Digest(byte fill) => Enumerable.Repeat(fill, 32).ToArray();
}
