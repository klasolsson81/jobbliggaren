using System.Text.Json.Serialization;

namespace Jobbliggaren.Application.Admin.Email.Dmarc;

/// <summary>What the DMARC region of <c>/admin/e-post</c> may show (#1981 PR 4a).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EmailDmarcStatus>))]
public enum EmailDmarcStatus
{
    /// <summary>At least one report in the window, after at least one successful run.</summary>
    Observed,

    /// <summary>The mailbox has been read, and no stored report overlaps the window. Carries no figure, never a zero.</summary>
    NoReports,

    /// <summary>No run has ever written its state: the mailbox has not been read yet.</summary>
    NotObservable,

    /// <summary>The latest run found no mailbox password configured in this deployment.</summary>
    NotConfigured,

    /// <summary>The latest run found a password pointer set but the password blank.</summary>
    Misconfigured,

    /// <summary>No run has ever succeeded. <see cref="EmailDmarcDto.Ingestion"/> names how the latest one ended.</summary>
    Failed,
}

/// <summary>
/// The DMARC reports for the fixed window as the browser receives them (#1981 PR 4a, ADR 0185). A closed union like the
/// other regions': the ingestion state once a run has read the mailbox, the window and the mailbox counts once one has
/// succeeded, and figures only when a report is in the window. An unknown figure is absent, never zero. The figures are
/// the reporters' own statements. A test pins the serialised key set.
/// </summary>
public sealed record EmailDmarcDto
{
    private EmailDmarcDto(
        EmailDmarcStatus status,
        DmarcIngestionDto? ingestion = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        DateTimeOffset? coveredFrom = null,
        DateTimeOffset? coveredTo = null,
        long? conflictingReports = null,
        DmarcMailboxCountsDto? mailbox = null,
        long? reports = null,
        long? reporters = null,
        long? messages = null,
        long? failingMessages = null,
        long? sourceCount = null,
        IReadOnlyList<DmarcSourceDto>? sources = null)
    {
        Status = status;
        Ingestion = ingestion;
        From = from;
        To = to;
        CoveredFrom = coveredFrom;
        CoveredTo = coveredTo;
        ConflictingReports = conflictingReports;
        Mailbox = mailbox;
        Reports = reports;
        Reporters = reporters;
        Messages = messages;
        FailingMessages = failingMessages;
        SourceCount = sourceCount;
        Sources = sources;
    }

    public EmailDmarcStatus Status { get; }

    /// <summary>How the latest run ended and when the latest success was. Present when observed, without reports, or failed.</summary>
    public DmarcIngestionDto? Ingestion { get; }

    /// <summary>The window's start. Present when observed or without reports.</summary>
    public DateTimeOffset? From { get; }

    /// <summary>The window's end, exclusive: the moment of the read.</summary>
    public DateTimeOffset? To { get; }

    /// <summary>The earliest start of an included report's period, which may lie before the window. Present when observed.</summary>
    public DateTimeOffset? CoveredFrom { get; }

    /// <summary>The latest end of an included report's period, exclusive. Present when observed.</summary>
    public DateTimeOffset? CoveredTo { get; }

    /// <summary>
    /// Reports in the window whose reporter and report id were stored with other content too. None of their versions is
    /// in any figure. Present when observed or without reports.
    /// </summary>
    public long? ConflictingReports { get; }

    /// <summary>Messages received in the window that stored nothing, and the messages waiting for a retry.</summary>
    public DmarcMailboxCountsDto? Mailbox { get; }

    /// <summary>The included reports. Present exactly when observed.</summary>
    public long? Reports { get; }

    /// <summary>The distinct reporters of the included reports, as each names itself.</summary>
    public long? Reporters { get; }

    /// <summary>The messages the included reports count, over every source.</summary>
    public long? Messages { get; }

    /// <summary>Of <see cref="Messages"/>, those that passed neither DKIM nor SPF aligned.</summary>
    public long? FailingMessages { get; }

    /// <summary>Every source and header-from pair in the included reports, not only those in <see cref="Sources"/>.</summary>
    public long? SourceCount { get; }

    /// <summary>The largest sources, at most <see cref="DmarcReportsEvaluator.MaxSources"/>, flagged first.</summary>
    public IReadOnlyList<DmarcSourceDto>? Sources { get; }

    public static EmailDmarcDto NotObservable() => new(EmailDmarcStatus.NotObservable);

    public static EmailDmarcDto NotConfigured() => new(EmailDmarcStatus.NotConfigured);

    public static EmailDmarcDto Misconfigured() => new(EmailDmarcStatus.Misconfigured);

    public static EmailDmarcDto Failed(DmarcIngestionDto ingestion)
    {
        ArgumentNullException.ThrowIfNull(ingestion);
        if (ingestion.LastSuccessAt is not null)
        {
            throw new ArgumentException("A mailbox that was read once is not shown as failed.", nameof(ingestion));
        }

        return new(EmailDmarcStatus.Failed, ingestion);
    }

    public static EmailDmarcDto NoReports(
        DmarcIngestionDto ingestion,
        DateTimeOffset from,
        DateTimeOffset to,
        long conflictingReports,
        DmarcMailboxCountsDto mailbox)
    {
        Succeeded(ingestion, from, to, conflictingReports, mailbox);
        return new(EmailDmarcStatus.NoReports, ingestion, from, to, conflictingReports: conflictingReports, mailbox: mailbox);
    }

    public static EmailDmarcDto Observed(
        DmarcIngestionDto ingestion,
        DateTimeOffset from,
        DateTimeOffset to,
        DmarcReportsTotals totals,
        DmarcMailboxCountsDto mailbox,
        IReadOnlyList<DmarcSourceDto> sources)
    {
        ArgumentNullException.ThrowIfNull(totals);
        ArgumentNullException.ThrowIfNull(sources);
        Succeeded(ingestion, from, to, totals.ConflictingReports, mailbox);
        if (totals.Reports < 1 || totals.Reporters < 1 || totals.Reporters > totals.Reports)
        {
            throw new ArgumentException("An observed window has a report, and no more reporters than reports.", nameof(totals));
        }

        if (totals.SourceCount < 1 || sources.Count != Math.Min(totals.SourceCount, DmarcReportsEvaluator.MaxSources))
        {
            throw new ArgumentException("The sources shown are every source, up to the cap.", nameof(sources));
        }

        if (totals.Messages < 1 || totals.FailingMessages < 0 || totals.FailingMessages > totals.Messages)
        {
            throw new ArgumentException("A report counts at least one message, and no more failing than counted.", nameof(totals));
        }

        var shownMessages = sources.Sum(source => source.Messages);
        var shownFailing = sources.Sum(source => source.FailingDmarc);
        var allShown = sources.Count == totals.SourceCount;
        if (shownMessages > totals.Messages || shownFailing > totals.FailingMessages
            || (allShown && (shownMessages != totals.Messages || shownFailing != totals.FailingMessages)))
        {
            throw new ArgumentException("The totals are the sums over every source, the sources shown included.", nameof(totals));
        }

        if (totals.CoveredFrom is not { } coveredFrom || totals.CoveredTo is not { } coveredTo
            || coveredFrom >= coveredTo || coveredFrom >= to || coveredTo <= from)
        {
            throw new ArgumentException("Included reports cover a period that overlaps the window.", nameof(totals));
        }

        return new(
            EmailDmarcStatus.Observed,
            ingestion,
            from,
            to,
            coveredFrom,
            coveredTo,
            totals.ConflictingReports,
            mailbox,
            totals.Reports,
            totals.Reporters,
            totals.Messages,
            totals.FailingMessages,
            totals.SourceCount,
            sources);
    }

    private static void Succeeded(
        DmarcIngestionDto ingestion, DateTimeOffset from, DateTimeOffset to, long conflictingReports, DmarcMailboxCountsDto mailbox)
    {
        ArgumentNullException.ThrowIfNull(ingestion);
        ArgumentNullException.ThrowIfNull(mailbox);
        if (ingestion.LastSuccessAt is null)
        {
            throw new ArgumentException("Figures and counts follow a successful run.", nameof(ingestion));
        }

        if (from >= to)
        {
            throw new ArgumentException("A window ends after it starts.", nameof(to));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(conflictingReports);
    }
}

/// <summary>
/// How the mailbox was last read. <see cref="LatestFailure"/> is the latest run's outcome when that run did not succeed;
/// <see cref="Stale"/> is decided by the backend's threshold, never by the browser's clock.
/// </summary>
public sealed record DmarcIngestionDto
{
    public DmarcIngestionDto(
        DateTimeOffset lastAttemptAt, DateTimeOffset? lastSuccessAt, DmarcRunOutcome? latestFailure, bool stale)
    {
        if (latestFailure is { } failure
            && (!Enum.IsDefined(failure) || failure is DmarcRunOutcome.Succeeded
                or DmarcRunOutcome.NotConfigured or DmarcRunOutcome.Misconfigured))
        {
            throw new ArgumentException("A latest failure is a run that tried to read the mailbox and failed.", nameof(latestFailure));
        }

        if (latestFailure is null && lastSuccessAt is null)
        {
            throw new ArgumentException("A latest run that succeeded is the latest success.", nameof(lastSuccessAt));
        }

        if (latestFailure == DmarcRunOutcome.Incomplete && lastSuccessAt is not null)
        {
            throw new ArgumentException("Incomplete is written only before any run has recorded an end.", nameof(latestFailure));
        }

        if (latestFailure is not null && !stale)
        {
            throw new ArgumentException("A mailbox whose latest run failed is stale.", nameof(stale));
        }

        LastAttemptAt = lastAttemptAt;
        LastSuccessAt = lastSuccessAt;
        LatestFailure = latestFailure;
        SignInResumesAt = latestFailure is { } backedOff ? DmarcOutcomes.SignInResumesAt(backedOff, lastAttemptAt) : null;
        Stale = stale;
    }

    public DateTimeOffset LastAttemptAt { get; }

    public DateTimeOffset? LastSuccessAt { get; }

    public DmarcRunOutcome? LatestFailure { get; }

    /// <summary>
    /// After a latest run whose sign-in backs off, the first instant a run may sign in again; otherwise null. The earliest
    /// moment, not a scheduled one: the run that signs in is the first one at or after it, so it may already have passed
    /// when it is read.
    /// </summary>
    public DateTimeOffset? SignInResumesAt { get; }

    public bool Stale { get; }
}

/// <summary>
/// Messages received in the window that stored nothing, by class, and every message still waiting for a retry. A
/// count of the mailbox's own ledger, so a zero is observed, not unknown.
/// </summary>
public sealed record DmarcMailboxCountsDto
{
    public DmarcMailboxCountsDto(long skipped, long rejected, long volumeCapped, long abandoned, long retryPending)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(skipped);
        ArgumentOutOfRangeException.ThrowIfNegative(rejected);
        ArgumentOutOfRangeException.ThrowIfNegative(volumeCapped);
        ArgumentOutOfRangeException.ThrowIfNegative(abandoned);
        ArgumentOutOfRangeException.ThrowIfNegative(retryPending);
        Skipped = skipped;
        Rejected = rejected;
        VolumeCapped = volumeCapped;
        Abandoned = abandoned;
        RetryPending = retryPending;
    }

    /// <summary>Held no part the decoder may read.</summary>
    public long Skipped { get; }

    /// <summary>The report part was read and the report refused, whole.</summary>
    public long Rejected { get; }

    /// <summary>Arrived after the day's storage cap was reached.</summary>
    public long VolumeCapped { get; }

    /// <summary>Given up after two crashes or the retry limit. The mailbox still holds them.</summary>
    public long Abandoned { get; }

    /// <summary>Waiting for a later run to try again, whenever received.</summary>
    public long RetryPending { get; }
}

/// <summary>
/// One sending server and header-from domain over the window's included reports. Each figure is a message count that
/// holds for every message in the row; <see cref="Flagged"/> is a failing count above zero.
/// </summary>
public sealed record DmarcSourceDto
{
    public DmarcSourceDto(DmarcSourceRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(row.Source);
        ArgumentException.ThrowIfNullOrWhiteSpace(row.HeaderFrom);
        if (row.Messages < 1
            || row.DkimAligned < 0 || row.DkimAligned > row.Messages
            || row.SpfAligned < 0 || row.SpfAligned > row.Messages
            || row.FailingDmarc < 0 || row.FailingDmarc > row.Messages)
        {
            throw new ArgumentException("A source counts at least one message, and each figure is a part of them.", nameof(row));
        }

        Address = row.Source.ToString();
        HeaderFrom = row.HeaderFrom;
        Messages = row.Messages;
        DkimAligned = row.DkimAligned;
        SpfAligned = row.SpfAligned;
        FailingDmarc = row.FailingDmarc;
    }

    /// <summary>The address in canonical form; an IPv4-mapped address is already the IPv4 address.</summary>
    public string Address { get; }

    public string HeaderFrom { get; }

    public long Messages { get; }

    public long DkimAligned { get; }

    public long SpfAligned { get; }

    /// <summary>Messages that passed neither DKIM nor SPF aligned.</summary>
    public long FailingDmarc { get; }

    public bool Flagged => FailingDmarc > 0;
}
