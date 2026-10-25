namespace Jobbliggaren.Application.Admin.Email.Dmarc;

/// <summary>
/// #1981 PR 4a (ADR 0185) — the read side of the stored DMARC reports, for <c>/admin/e-post</c>. Application defines the
/// port; Infrastructure implements it over the same tables, in one consistent read. Api-only; its one consumer is the
/// reports query handler. A database failure is an exception.
/// <para>
/// A report is in the window when its period overlaps it, half-open. A reporter and report id stored with more than one
/// content digest is a conflict: none of its versions counts towards any figure, and it is counted once. Ledger counts
/// are by receipt within the window, a different time axis from the reports' periods.
/// </para>
/// </summary>
public interface IDmarcReportsSource
{
    ValueTask<DmarcReportsRead> ReadAsync(
        DateTimeOffset windowStart, DateTimeOffset windowEnd, int maxSources, CancellationToken cancellationToken);
}

/// <summary>One read: the state row, the largest sources, the totals over every source, and the ledger counts.</summary>
public sealed record DmarcReportsRead(
    DmarcRunState? State,
    IReadOnlyList<DmarcSourceRow> Sources,
    DmarcReportsTotals Totals,
    IReadOnlyList<DmarcOutcomeCount> MessagesReceivedInWindow,
    long RetryPending);

/// <summary>
/// One source and header-from domain over the window's non-conflicting reports. Each figure is a message count; the
/// order is flagged first, then failing, messages, address (family, then bytes) and domain.
/// </summary>
public sealed record DmarcSourceRow(
    ReportedSourceAddress Source,
    string HeaderFrom,
    long Messages,
    long DkimAligned,
    long SpfAligned,
    long FailingDmarc);

/// <summary>
/// Totals over every source in the window, not only the rows returned. <see cref="CoveredFrom"/> and
/// <see cref="CoveredTo"/> span the included reports' periods, and are null when none is included.
/// </summary>
public sealed record DmarcReportsTotals(
    long Reports,
    long Reporters,
    long SourceCount,
    long Messages,
    long FailingMessages,
    long ConflictingReports,
    DateTimeOffset? CoveredFrom,
    DateTimeOffset? CoveredTo);

/// <summary>How many messages received in the window came to one ledger outcome.</summary>
public sealed record DmarcOutcomeCount(DmarcMessageOutcome Outcome, long Count);
