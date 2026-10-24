namespace Jobbliggaren.Application.Admin.Email.Dmarc;

/// <summary>
/// #1981 PR 4a (ADR 0183) — one aggregate report as the decoder read it: syntax only, nothing accepted yet. Only the
/// fields ingestion may keep are carried; the decoder never surfaces the reporter's contact address, extra contact
/// information, envelope identifiers, raw authentication results, disposition, reasons, comments, errors or extension
/// elements. <see cref="DmarcAggregateReport.Accept"/> decides whether it is stored.
/// </summary>
public sealed record DmarcReportCandidate(
    string Reporter,
    string ReportId,
    string PolicyDomain,
    long BeginUnixSeconds,
    long EndUnixSeconds,
    IReadOnlyList<DmarcRecordCandidate> Records,
    byte[] ContentSha256);

/// <summary>One record as read: the source as written, the claimed count, the header-from domain and both aligned results.</summary>
public sealed record DmarcRecordCandidate(
    string SourceIp,
    long Count,
    string HeaderFrom,
    string Dkim,
    string Spf);
