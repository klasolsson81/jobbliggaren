namespace Jobbliggaren.Application.Admin.Email.Dmarc;

/// <summary>
/// #1981 PR 4a (ADR 0184) — the ingestion side of the stored DMARC reports: the reports, the per-message ledger and the
/// one state row with the cursor. Application defines the port; Infrastructure implements it over its own tables. Its
/// one consumer is the ingestion job. A database failure is an exception, as on every database write.
/// </summary>
public interface IDmarcReportStore
{
    /// <summary>The state row, or <see langword="null"/> when no run has ever written one.</summary>
    ValueTask<DmarcRunState?> ReadStateAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Writes the state row as <see cref="DmarcRunOutcome.Incomplete"/> at <paramref name="attemptAt"/> when none
    /// exists, so a first run that dies after it has claimed a message is not read as one that never ran.
    /// </summary>
    ValueTask EnsureStateAsync(DateTimeOffset attemptAt, CancellationToken cancellationToken);

    /// <summary>
    /// Counts one crash for every message still <see cref="DmarcMessageOutcome.InProgress"/>: it goes back to
    /// <see cref="DmarcMessageOutcome.RetryPending"/>, or to <see cref="DmarcMessageOutcome.Abandoned"/> once it has
    /// reached <see cref="DmarcIngestionLimits.MaxCrashAttempts"/>. One transaction.
    /// </summary>
    ValueTask<DmarcCrashSweep> SweepCrashesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Moves the cursor into <paramref name="epoch"/>. When it was in another epoch, one transaction sets it to the new
    /// epoch at position 0 and deletes the old epoch's ledger rows; stored reports stay, and the content key keeps a
    /// rescan from storing one twice. Returns whether the epoch changed.
    /// </summary>
    ValueTask<bool> EnterEpochAsync(long epoch, CancellationToken cancellationToken);

    /// <summary>
    /// Claims a new message before any of its content is fetched: one transaction writes its ledger row
    /// <see cref="DmarcMessageOutcome.InProgress"/> and advances the cursor to it.
    /// </summary>
    ValueTask ClaimAsync(long epoch, long position, DateTimeOffset receivedAt, CancellationToken cancellationToken);

    /// <summary>Advances the cursor past a message that gets no ledger row (received before the retention cutoff).</summary>
    ValueTask AdvanceCursorAsync(long epoch, long position, CancellationToken cancellationToken);

    /// <summary>
    /// The epoch's messages waiting for a retry and received at or after <paramref name="receivedFrom"/> (the retention
    /// cutoff), oldest position first, at most <paramref name="max"/>.
    /// </summary>
    ValueTask<IReadOnlyList<DmarcPendingMessage>> ListRetriesAsync(
        long epoch, DateTimeOffset receivedFrom, int max, CancellationToken cancellationToken);

    /// <summary>
    /// Claims a waiting message for one more try. At <see cref="DmarcIngestionLimits.RetryLimit"/> retries it becomes
    /// <see cref="DmarcMessageOutcome.Abandoned"/> instead, and this returns <see langword="false"/>.
    /// </summary>
    ValueTask<bool> ClaimRetryAsync(long epoch, long position, CancellationToken cancellationToken);

    /// <summary>Ends a claimed message's turn with an outcome that stores nothing, or hands it back for a retry.</summary>
    ValueTask SetOutcomeAsync(long epoch, long position, DmarcMessageOutcome outcome, CancellationToken cancellationToken);

    /// <summary>
    /// Stores an accepted report for a claimed message, inside the day's caps, and records the outcome in the same
    /// transaction: <see cref="DmarcMessageOutcome.Stored"/>, <see cref="DmarcMessageOutcome.Duplicate"/>,
    /// <see cref="DmarcMessageOutcome.StoredConflicting"/>, <see cref="DmarcMessageOutcome.VolumeCapped"/> or
    /// <see cref="DmarcMessageOutcome.RejectedByStore"/>.
    /// </summary>
    ValueTask<DmarcMessageOutcome> StoreAsync(
        long epoch, long position, DmarcAggregateReport report, CancellationToken cancellationToken);

    /// <summary>
    /// Records how the run that started at <paramref name="attemptAt"/> ended. A success also moves
    /// <see cref="DmarcRunState.LastSuccessAt"/>.
    /// </summary>
    ValueTask RecordAttemptAsync(DateTimeOffset attemptAt, DmarcRunOutcome outcome, CancellationToken cancellationToken);

    /// <summary>Refreshes the planner statistics of the tables a run wrote (AGENTS.md §3.6). Outside any transaction.</summary>
    ValueTask AnalyzeAsync(CancellationToken cancellationToken);
}

/// <summary>The one state row: the latest run, the latest success, and the cursor.</summary>
public sealed record DmarcRunState(
    DateTimeOffset LastAttemptAt,
    DmarcRunOutcome LastOutcome,
    DateTimeOffset? LastSuccessAt,
    DmarcCursor? Cursor);

/// <summary>The last position claimed in an epoch.</summary>
public sealed record DmarcCursor(long Epoch, long Position);

/// <summary>A message waiting for a retry.</summary>
public sealed record DmarcPendingMessage(long Position, DateTimeOffset ReceivedAt, int Retries);

/// <summary>What one crash sweep did.</summary>
public sealed record DmarcCrashSweep(int Requeued, int Abandoned);
