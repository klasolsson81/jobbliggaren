namespace Jobbliggaren.Application.Admin.Email.Dmarc;

/// <summary>
/// #1981 PR 4a (ADR 0183) — turns one read of the stored DMARC reports into what the region may show. Pure and static;
/// it throws on a read no source can produce.
/// <para>
/// <b>The states, in precedence order:</b> no state row is <see cref="EmailDmarcStatus.NotObservable"/>; a latest run
/// that found no password, or a blank one, is <see cref="EmailDmarcStatus.NotConfigured"/> or
/// <see cref="EmailDmarcStatus.Misconfigured"/>, whatever came before; a mailbox never read successfully is
/// <see cref="EmailDmarcStatus.Failed"/>; otherwise the window has reports (<see cref="EmailDmarcStatus.Observed"/>) or
/// none (<see cref="EmailDmarcStatus.NoReports"/>). A run that failed after an earlier success keeps the stored figures,
/// which hold for the period they cover, and is shown stale with its outcome.
/// </para>
/// </summary>
public static class DmarcReportsEvaluator
{
    /// <summary>The region's own window, ending at the read: not driven by the delivery region's period.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(7);

    /// <summary>The largest sources one read returns.</summary>
    public const int MaxSources = 25;

    /// <summary>Three missed hourly runs: a latest success older than this is stale.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(3);

    /// <summary>The window, half-open: from <see cref="Window"/> before <paramref name="now"/> up to it.</summary>
    public static (DateTimeOffset From, DateTimeOffset To) WindowOf(DateTimeOffset now)
    {
        var to = now.ToUniversalTime();
        return (to - Window, to);
    }

    public static EmailDmarcDto Evaluate(DmarcReportsRead read, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(read.Totals);
        ArgumentNullException.ThrowIfNull(read.Sources);
        ArgumentNullException.ThrowIfNull(read.MessagesReceivedInWindow);
        if (read.State is not { } state)
        {
            return EmailDmarcDto.NotObservable();
        }

        if (!Enum.IsDefined(state.LastOutcome))
        {
            throw new ArgumentOutOfRangeException(nameof(read), state.LastOutcome, "A run outcome this release does not know.");
        }

        switch (state.LastOutcome)
        {
            case DmarcRunOutcome.NotConfigured:
                return EmailDmarcDto.NotConfigured();
            case DmarcRunOutcome.Misconfigured:
                return EmailDmarcDto.Misconfigured();
        }

        if (state.LastSuccessAt is null && state.LastOutcome == DmarcRunOutcome.Succeeded)
        {
            throw new ArgumentException("A successful run is the latest success.", nameof(read));
        }

        var latestFailure = state.LastOutcome == DmarcRunOutcome.Succeeded ? (DmarcRunOutcome?)null : state.LastOutcome;
        var stale = latestFailure is not null || now - state.LastSuccessAt > StaleAfter;
        var ingestion = new DmarcIngestionDto(state.LastAttemptAt, state.LastSuccessAt, latestFailure, stale);
        if (state.LastSuccessAt is null)
        {
            return EmailDmarcDto.Failed(ingestion);
        }

        var (from, to) = WindowOf(now);
        var mailbox = MailboxOf(read);
        var totals = read.Totals;
        if (totals.Reports > 0)
        {
            return EmailDmarcDto.Observed(
                ingestion, from, to, totals, mailbox, [.. read.Sources.Select(row => new DmarcSourceDto(row))]);
        }

        if (totals is not { Reporters: 0, SourceCount: 0, Messages: 0, FailingMessages: 0, CoveredFrom: null, CoveredTo: null }
            || read.Sources.Count > 0)
        {
            throw new ArgumentException("A window without a report has no figure.", nameof(read));
        }

        return EmailDmarcDto.NoReports(ingestion, from, to, totals.ConflictingReports, mailbox);
    }

    private static DmarcMailboxCountsDto MailboxOf(DmarcReportsRead read)
    {
        long skipped = 0, rejected = 0, volumeCapped = 0, abandoned = 0;
        foreach (var count in read.MessagesReceivedInWindow)
        {
            ArgumentNullException.ThrowIfNull(count);
            if (!Enum.IsDefined(count.Outcome))
            {
                throw new ArgumentOutOfRangeException(nameof(read), count.Outcome, "A message outcome this release does not know.");
            }

            ArgumentOutOfRangeException.ThrowIfNegative(count.Count);
            if (DmarcOutcomeClasses.IsSkipped(count.Outcome))
            {
                skipped += count.Count;
            }
            else if (DmarcOutcomeClasses.IsRejected(count.Outcome))
            {
                rejected += count.Count;
            }
            else if (count.Outcome == DmarcMessageOutcome.VolumeCapped)
            {
                volumeCapped += count.Count;
            }
            else if (count.Outcome == DmarcMessageOutcome.Abandoned)
            {
                abandoned += count.Count;
            }
        }

        return new DmarcMailboxCountsDto(skipped, rejected, volumeCapped, abandoned, read.RetryPending);
    }
}
