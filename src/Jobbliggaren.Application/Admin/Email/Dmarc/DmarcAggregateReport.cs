using System.Globalization;
using System.Text;

namespace Jobbliggaren.Application.Admin.Email.Dmarc;

/// <summary>
/// #1981 PR 4a (ADR 0184) — one aggregate report ingestion has accepted. It exists only through <see cref="Accept"/>,
/// and the store takes nothing else, so a report that broke a bound cannot be stored.
/// <para>
/// A report is the reporter's own statement and is never verified (anyone can mail the report address). What
/// acceptance guards is what the page may show: a bound broken by any one record refuses the whole report, because a
/// partly accepted report would present a subset as if it were what the reporter said.
/// </para>
/// </summary>
public sealed class DmarcAggregateReport
{
    private const int Sha256Length = 32;

    private readonly byte[] _contentSha256;

    private DmarcAggregateReport(
        string reporter,
        string reportId,
        byte[] contentSha256,
        DateTimeOffset beginsAt,
        DateTimeOffset endsAt,
        DateTimeOffset receivedAt,
        IReadOnlyList<DmarcReportRecord> records)
    {
        Reporter = reporter;
        ReportId = reportId;
        _contentSha256 = contentSha256;
        BeginsAt = beginsAt;
        EndsAt = endsAt;
        ReceivedAt = receivedAt;
        Records = records;
    }

    /// <summary>The reporter's own name for itself (<c>org_name</c>). Free text a sender writes; never logged.</summary>
    public string Reporter { get; }

    /// <summary>The reporter's id for the report. Never logged.</summary>
    public string ReportId { get; }

    /// <summary>SHA-256 over the decompressed XML the decoder read.</summary>
    public ReadOnlySpan<byte> ContentSha256 => _contentSha256;

    public DateTimeOffset BeginsAt { get; }

    /// <summary>The end of the period, exclusive.</summary>
    public DateTimeOffset EndsAt { get; }

    /// <summary>When the mailbox received the message; retention counts from here.</summary>
    public DateTimeOffset ReceivedAt { get; }

    public IReadOnlyList<DmarcReportRecord> Records { get; }

    public byte[] CopyContentSha256() => (byte[])_contentSha256.Clone();

    /// <summary>
    /// Accepts <paramref name="candidate"/> or names the first bound it breaks. Pure and total: it never throws for
    /// anything a report can contain.
    /// </summary>
    public static DmarcAcceptance Accept(DmarcReportCandidate candidate, DateTimeOffset receivedAt)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(candidate.Records);
        if (candidate.ContentSha256 is not { Length: Sha256Length })
            throw new ArgumentException("A candidate carries the SHA-256 of the XML it was read from.", nameof(candidate));

        if (!IsPolicyDomain(candidate.PolicyDomain))
            return new DmarcAcceptance.Rejected(DmarcRejection.PolicyDomain);

        if (TextBound(candidate.Reporter) is { } reporterBreak)
            return new DmarcAcceptance.Rejected(reporterBreak);
        if (TextBound(candidate.ReportId) is { } reportIdBreak)
            return new DmarcAcceptance.Rejected(reportIdBreak);

        if (candidate.Records.Count == 0)
            return new DmarcAcceptance.Rejected(DmarcRejection.NoRecords);
        if (candidate.Records.Count > DmarcIngestionLimits.MaxRecordsPerReport)
            return new DmarcAcceptance.Rejected(DmarcRejection.TooManyRecords);

        var received = receivedAt.ToUniversalTime();
        if (PeriodOf(candidate, received) is not { } period)
            return new DmarcAcceptance.Rejected(DmarcRejection.ImplausibleRange);

        var records = new List<DmarcReportRecord>(candidate.Records.Count);
        foreach (var record in candidate.Records)
        {
            ArgumentNullException.ThrowIfNull(record);
            if (!ReportedSourceAddress.TryParse(record.SourceIp, out var source))
                return new DmarcAcceptance.Rejected(DmarcRejection.MalformedSource);
            if (record.Count is < 1 or > DmarcIngestionLimits.MaxRecordMessageCount)
                return new DmarcAcceptance.Rejected(DmarcRejection.CountOutOfRange);
            if (HeaderFromOf(record.HeaderFrom) is not { } headerFrom)
                return new DmarcAcceptance.Rejected(DmarcRejection.HeaderFromOutsideDomain);

            records.Add(new DmarcReportRecord(
                source!, (int)record.Count, headerFrom, AlignmentOf(record.Dkim), AlignmentOf(record.Spf)));
        }

        return new DmarcAcceptance.Accepted(new DmarcAggregateReport(
            candidate.Reporter,
            candidate.ReportId,
            (byte[])candidate.ContentSha256.Clone(),
            period.Begins,
            period.Ends,
            received,
            records));
    }

    /// <summary>
    /// The report's period, or <see langword="null"/> when it is not plausible. An end on the last second of an hour
    /// (Google's and Yahoo's inclusive <c>begin + 86399</c>) becomes the exclusive end one second later; Microsoft's
    /// <c>begin + 86400</c> is already exclusive.
    /// </summary>
    private static (DateTimeOffset Begins, DateTimeOffset Ends)? PeriodOf(DmarcReportCandidate candidate, DateTimeOffset received)
    {
        var begin = candidate.BeginUnixSeconds;
        var end = candidate.EndUnixSeconds;
        if (begin < 0 || end <= begin || end > DateTimeOffset.MaxValue.ToUnixTimeSeconds() - 1)
            return null;

        if ((end - begin) % 3600 == 3599)
            end += 1;

        var begins = DateTimeOffset.FromUnixTimeSeconds(begin);
        var ends = DateTimeOffset.FromUnixTimeSeconds(end);
        if (ends - begins > DmarcIngestionLimits.MaxReportSpan || ends > received + DmarcIngestionLimits.MaxEndAfterReceipt)
            return null;

        return (begins, ends);
    }

    private static bool IsPolicyDomain(string? domain) =>
        domain is not null
        && string.Equals(domain.TrimEnd('.'), DmarcIngestionLimits.PolicyDomain, StringComparison.OrdinalIgnoreCase);

    /// <summary>A kept text field is non-blank, bounded, and carries no control or format character (bidi included).</summary>
    private static DmarcRejection? TextBound(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return DmarcRejection.ForbiddenCharacter;
        if (text.Length > DmarcIngestionLimits.MaxFieldLength)
            return DmarcRejection.FieldTooLong;

        foreach (var rune in text.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.Control or UnicodeCategory.Format
                or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned
                or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
                return DmarcRejection.ForbiddenCharacter;
        }

        if (text.Contains('�', StringComparison.Ordinal))
            return DmarcRejection.ForbiddenCharacter;

        return null;
    }

    /// <summary>
    /// The header-from domain in lower case, when it is LDH labels equal to the policy domain or under it; otherwise
    /// <see langword="null"/>. Only such a domain is kept, so nothing else a sender writes reaches the page.
    /// </summary>
    private static string? HeaderFromOf(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        var domain = text.TrimEnd('.').ToLowerInvariant();
        if (domain.Length is 0 or > DmarcIngestionLimits.MaxHeaderFromLength)
            return null;

        foreach (var label in domain.Split('.'))
        {
            if (label.Length is 0 or > 63 || label[0] == '-' || label[^1] == '-')
                return null;
            foreach (var c in label)
            {
                if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'))
                    return null;
            }
        }

        var policy = DmarcIngestionLimits.PolicyDomain;
        return domain == policy || domain.EndsWith("." + policy, StringComparison.Ordinal) ? domain : null;
    }

    private static DmarcAlignment AlignmentOf(string? result) => result?.Trim().ToLowerInvariant() switch
    {
        "pass" => DmarcAlignment.Pass,
        "fail" => DmarcAlignment.Fail,
        _ => DmarcAlignment.Other,
    };
}

/// <summary>One accepted record. The source is personal data; nothing else here is.</summary>
public sealed record DmarcReportRecord(
    ReportedSourceAddress Source,
    int MessageCount,
    string HeaderFrom,
    DmarcAlignment Dkim,
    DmarcAlignment Spf)
{
    /// <summary>A message fails DMARC when neither mechanism passed aligned (RFC 7489 §4.2; RFC 9989).</summary>
    public bool FailsDmarc => Dkim != DmarcAlignment.Pass && Spf != DmarcAlignment.Pass;
}

/// <summary>What <see cref="DmarcAggregateReport.Accept"/> decides: a closed set.</summary>
public abstract record DmarcAcceptance
{
    private DmarcAcceptance() { }

    public sealed record Accepted(DmarcAggregateReport Report) : DmarcAcceptance;

    public sealed record Rejected(DmarcRejection Reason) : DmarcAcceptance;
}
