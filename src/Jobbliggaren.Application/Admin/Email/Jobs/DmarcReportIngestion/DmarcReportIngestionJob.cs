using Jobbliggaren.Application.Admin.Email.Dmarc;
using Jobbliggaren.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Application.Admin.Email.Jobs.DmarcReportIngestion;

/// <summary>
/// #1981 PR 4a (ADR 0184) — reads the DMARC report mailbox read-only and stores what it may keep. One run, in a fixed
/// order: the sign-in back-off, the crash sweep, the mailbox, the epoch, the new mail in position order, the retries
/// last, the run's outcome, then ANALYZE.
/// <para>
/// <b>Every message is claimed before any of its content is fetched</b>, so the cursor never waits on it. A refusal or a
/// session fault hands the claim back for a retry; content that cannot be read ends it for good. A row still
/// in progress at the start of a run counts one crash; a caught failure, a session fault and a graceful shutdown count
/// none. A database failure propagates and leaves its claim in progress, which the next run counts.
/// </para>
/// <para>
/// The run deadline ends the run as <see cref="DmarcRunOutcome.Timeout"/>; the caller's cancellation (the host shutting
/// down) hands the claim in hand back and propagates. Both writes go under a fresh, bounded token. Logs carry event ids,
/// outcomes and counts only: never an address, a reporter, a report id, a position or an epoch.
/// </para>
/// </summary>
public sealed partial class DmarcReportIngestionJob(
    IDmarcMailbox mailbox,
    IDmarcReportDecoder decoder,
    IDmarcReportStore store,
    IDateTimeProvider clock,
    ILogger<DmarcReportIngestionJob> logger)
{
    private readonly TimeSpan _runDeadline = DmarcIngestionLimits.RunDeadline;

    /// <summary>A shorter deadline, so a test can reach the deadline path without waiting five minutes.</summary>
    internal DmarcReportIngestionJob(
        IDmarcMailbox mailbox,
        IDmarcReportDecoder decoder,
        IDmarcReportStore store,
        IDateTimeProvider clock,
        ILogger<DmarcReportIngestionJob> logger,
        TimeSpan runDeadline)
        : this(mailbox, decoder, store, clock, logger) => _runDeadline = runDeadline;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var runStart = clock.UtcNow;
        var state = await store.ReadStateAsync(cancellationToken);
        if (state is { LastOutcome: DmarcRunOutcome.AuthRefused }
            && state.LastAttemptAt > runStart - DmarcIngestionLimits.AuthRefusedBackoff)
        {
            LogBackedOff();
            return;
        }

        var sweep = await store.SweepCrashesAsync(cancellationToken);
        if (sweep.Requeued > 0 || sweep.Abandoned > 0)
        {
            LogCrashesSwept(sweep.Requeued, sweep.Abandoned);
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_runDeadline);
        var run = new Run();
        try
        {
            await IngestAsync(state, runStart, run, deadline.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            using var write = new CancellationTokenSource(DmarcIngestionLimits.ShutdownWriteTimeout);
            await HandBackAsync(run, write.Token);
            throw;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            using var write = new CancellationTokenSource(DmarcIngestionLimits.ShutdownWriteTimeout);
            await HandBackAsync(run, write.Token);
            if (!run.Recorded)
            {
                await store.RecordAttemptAsync(runStart, DmarcRunOutcome.Timeout, write.Token);
                LogRunSummary(run, DmarcRunOutcome.Timeout);
                LogRunEnded(LogLevel.Warning, DmarcRunOutcome.Timeout);
            }
        }
    }

    private async Task IngestAsync(DmarcRunState? state, DateTimeOffset runStart, Run run, CancellationToken cancellationToken)
    {
        var open = await mailbox.OpenAsync(cancellationToken);
        if (open is not DmarcMailboxOpen.Opened opened)
        {
            var outcome = open switch
            {
                DmarcMailboxOpen.NotConfigured => DmarcRunOutcome.NotConfigured,
                DmarcMailboxOpen.Misconfigured => DmarcRunOutcome.Misconfigured,
                DmarcMailboxOpen.Failed failed => DmarcOutcomes.RunOutcomeOf(failed.Fault),
                _ => throw new InvalidOperationException("An open outcome this release does not know."),
            };
            await store.RecordAttemptAsync(runStart, outcome, cancellationToken);
            run.Recorded = true;
            LogRunEnded(outcome == DmarcRunOutcome.NotConfigured ? LogLevel.Information : LogLevel.Warning, outcome);
            return;
        }

        await using var session = opened.Session;
        await store.EnsureStateAsync(runStart, cancellationToken);
        run.Epoch = session.Epoch;
        var epochChanged = await store.EnterEpochAsync(run.Epoch, cancellationToken);
        var position = epochChanged || state?.Cursor is not { } cursor ? 0 : cursor.Position;

        var fault = await IngestNewAsync(session, position, runStart, run, cancellationToken)
            ?? await RetryAsync(session, runStart, run, cancellationToken);

        var runOutcome = fault is { } ended ? DmarcOutcomes.RunOutcomeOf(ended) : DmarcRunOutcome.Succeeded;
        await store.RecordAttemptAsync(runStart, runOutcome, cancellationToken);
        run.Recorded = true;
        LogRunSummary(run, runOutcome);
        if (fault is not null)
        {
            LogRunEnded(LogLevel.Warning, runOutcome);
            return;
        }

        if (run.Claimed > 0 && run.InHand is null)
        {
            await store.AnalyzeAsync(cancellationToken);
        }
    }

    /// <returns>The session fault that ended the run, or <see langword="null"/> when every listed message was handled.</returns>
    private async ValueTask<DmarcSessionFault?> IngestNewAsync(
        IDmarcMailboxSession session, long position, DateTimeOffset runStart, Run run, CancellationToken cancellationToken)
    {
        IReadOnlyList<DmarcMailboxEntry> entries;
        switch (await session.ListAfterAsync(position, DmarcIngestionLimits.PerRunMessageCap, cancellationToken))
        {
            case DmarcMailboxListing.Listed listed:
                entries = listed.Entries;
                break;
            case DmarcMailboxListing.Failed failed:
                return failed.Fault;
            default:
                throw new InvalidOperationException("A listing this release does not know.");
        }

        var cutoff = DmarcRetention.CutoffOf(runStart);
        foreach (var entry in entries)
        {
            var receivedAt = DmarcRetention.ReceivedAtOf(entry.ReceivedAt, runStart);
            if (receivedAt < cutoff)
            {
                await store.AdvanceCursorAsync(run.Epoch, entry.Position, cancellationToken);
                run.Expired++;
                continue;
            }

            await store.ClaimAsync(run.Epoch, entry.Position, receivedAt, cancellationToken);
            run.Take(entry.Position);
            run.NewClaimed++;
            if (entry.Size > DmarcIngestionLimits.MaxMessageBytes)
            {
                await EndTurnAsync(run, DmarcMessageOutcome.SkippedTooLarge, cancellationToken);
                continue;
            }

            if (await ReadClaimedAsync(session, entry, receivedAt, run, cancellationToken) is { } fault)
            {
                return fault;
            }
        }

        return null;
    }

    /// <summary>
    /// The epoch's waiting messages, oldest first, in what the cap leaves after the new mail. A message this run already
    /// tried waits for the next run, so no command is sent twice in one run. One received before the retention cutoff is
    /// left for the retention job: a retry never stores what retention would delete.
    /// </summary>
    private async ValueTask<DmarcSessionFault?> RetryAsync(
        IDmarcMailboxSession session, DateTimeOffset runStart, Run run, CancellationToken cancellationToken)
    {
        var room = DmarcIngestionLimits.PerRunMessageCap - run.NewClaimed;
        if (room < 1)
        {
            return null;
        }

        var cutoff = DmarcRetention.CutoffOf(runStart);
        foreach (var pending in await store.ListRetriesAsync(run.Epoch, room, cancellationToken))
        {
            if (run.Tried(pending.Position))
            {
                continue;
            }

            if (pending.ReceivedAt < cutoff)
            {
                run.Expired++;
                continue;
            }

            if (!await store.ClaimRetryAsync(run.Epoch, pending.Position, cancellationToken))
            {
                run.Abandoned++;
                continue;
            }

            run.Take(pending.Position);
            run.Retried++;
            var entry = new DmarcMailboxEntry(pending.Position, pending.ReceivedAt, size: 0);
            if (await ReadClaimedAsync(session, entry, pending.ReceivedAt, run, cancellationToken) is { } fault)
            {
                return fault;
            }
        }

        return null;
    }

    private async ValueTask<DmarcSessionFault?> ReadClaimedAsync(
        IDmarcMailboxSession session,
        DmarcMailboxEntry entry,
        DateTimeOffset receivedAt,
        Run run,
        CancellationToken cancellationToken)
    {
        switch (await session.FetchReportAsync(entry, cancellationToken))
        {
            case DmarcMailboxFetch.Fetched fetched:
                await StoreOrRejectAsync(fetched, receivedAt, run, cancellationToken);
                return null;
            case DmarcMailboxFetch.Skipped skipped:
                await EndTurnAsync(run, DmarcOutcomes.OutcomeOf(skipped.Reason), cancellationToken);
                return null;
            case DmarcMailboxFetch.Refused:
                await EndTurnAsync(run, DmarcMessageOutcome.RetryPending, cancellationToken);
                return null;
            case DmarcMailboxFetch.Failed failed:
                await EndTurnAsync(run, DmarcMessageOutcome.RetryPending, cancellationToken);
                return failed.Fault;
            default:
                throw new InvalidOperationException("A fetch outcome this release does not know.");
        }
    }

    private async ValueTask StoreOrRejectAsync(
        DmarcMailboxFetch.Fetched fetched, DateTimeOffset receivedAt, Run run, CancellationToken cancellationToken)
    {
        var acceptance = decoder.Decode(fetched.Content, fetched.Kind) switch
        {
            DmarcDecoding.Decoded decoded => DmarcAggregateReport.Accept(decoded.Candidate, receivedAt),
            DmarcDecoding.Malformed => null,
            _ => throw new InvalidOperationException("A decoding this release does not know."),
        };

        switch (acceptance)
        {
            case null:
                await EndTurnAsync(run, DmarcMessageOutcome.RejectedMalformed, cancellationToken);
                break;
            case DmarcAcceptance.Rejected rejected:
                await EndTurnAsync(run, DmarcOutcomes.OutcomeOf(rejected.Reason), cancellationToken);
                break;
            case DmarcAcceptance.Accepted accepted:
                var stored = await store.StoreAsync(run.Epoch, run.InHandPosition, accepted.Report, cancellationToken);
                run.Ended(stored);
                break;
            default:
                throw new InvalidOperationException("An acceptance this release does not know.");
        }
    }

    private async ValueTask EndTurnAsync(Run run, DmarcMessageOutcome outcome, CancellationToken cancellationToken)
    {
        await store.SetOutcomeAsync(run.Epoch, run.InHandPosition, outcome, cancellationToken);
        run.Ended(outcome);
    }

    private async ValueTask HandBackAsync(Run run, CancellationToken cancellationToken)
    {
        if (run.InHand is not null)
        {
            await EndTurnAsync(run, DmarcMessageOutcome.RetryPending, cancellationToken);
        }
    }

    private void LogRunSummary(Run run, DmarcRunOutcome outcome) => LogRunSummary(
        outcome,
        run.Of(DmarcMessageOutcome.Stored),
        run.Of(DmarcMessageOutcome.Duplicate),
        run.Of(DmarcMessageOutcome.StoredConflicting),
        run.Of(DmarcMessageOutcome.VolumeCapped),
        run.Skipped,
        run.Rejected,
        run.Of(DmarcMessageOutcome.RetryPending),
        run.Expired,
        run.Retried,
        run.Abandoned);

    [LoggerMessage(3120, LogLevel.Information,
        "[DmarcReportIngestion] Run ended {Outcome}: {Stored} stored, {Duplicates} duplicate, {Conflicting} stored "
        + "conflicting, {VolumeCapped} over the daily cap, {Skipped} skipped, {Rejected} rejected, {HandedBack} handed "
        + "back for a retry, {Expired} past retention, {Retried} retried, {Abandoned} abandoned")]
    private partial void LogRunSummary(
        DmarcRunOutcome outcome,
        int stored,
        int duplicates,
        int conflicting,
        int volumeCapped,
        int skipped,
        int rejected,
        int handedBack,
        int expired,
        int retried,
        int abandoned);

    [LoggerMessage(3121, LogLevel.Information,
        "[DmarcReportIngestion] The latest sign-in was refused within the back-off, so the mailbox is not opened")]
    private partial void LogBackedOff();

    [LoggerMessage(EventId = 3122, Message = "[DmarcReportIngestion] Run ended without a success: {Outcome}")]
    private partial void LogRunEnded(LogLevel level, DmarcRunOutcome outcome);

    [LoggerMessage(3123, LogLevel.Warning,
        "[DmarcReportIngestion] Earlier runs left messages in progress: {Requeued} handed back, {Abandoned} abandoned")]
    private partial void LogCrashesSwept(int requeued, int abandoned);

    /// <summary>One run's progress: the claim in hand and what each message came to.</summary>
    private sealed class Run
    {
        private readonly Dictionary<DmarcMessageOutcome, int> _outcomes = [];
        private readonly HashSet<long> _tried = [];

        public long Epoch { get; set; }

        public long? InHand { get; private set; }

        public long InHandPosition =>
            InHand ?? throw new InvalidOperationException("No message is claimed, so no outcome can end its turn.");

        public bool Recorded { get; set; }

        public int NewClaimed { get; set; }

        public int Retried { get; set; }

        public int Claimed => NewClaimed + Retried;

        public int Expired { get; set; }

        public int Abandoned { get; set; }

        public int Skipped => Sum(DmarcOutcomeClasses.IsSkipped);

        public int Rejected => Sum(DmarcOutcomeClasses.IsRejected);

        public void Take(long position)
        {
            InHand = position;
            _tried.Add(position);
        }

        public bool Tried(long position) => _tried.Contains(position);

        public void Ended(DmarcMessageOutcome outcome)
        {
            _outcomes[outcome] = Of(outcome) + 1;
            InHand = null;
        }

        public int Of(DmarcMessageOutcome outcome) => _outcomes.GetValueOrDefault(outcome);

        private int Sum(Func<DmarcMessageOutcome, bool> member) =>
            _outcomes.Where(pair => member(pair.Key)).Sum(pair => pair.Value);
    }
}
