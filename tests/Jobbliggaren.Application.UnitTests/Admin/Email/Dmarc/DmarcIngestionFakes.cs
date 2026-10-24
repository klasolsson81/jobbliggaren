using Jobbliggaren.Application.Admin.Email.Dmarc;
using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Application.UnitTests.Admin.Email.Dmarc;

// Hand-rolled fakes of the DMARC ingestion ports. Each keeps the port's documented contract in memory and throws on a
// call the contract does not allow, so a job that misuses a port fails here rather than against the database. The
// values the scripted mailbox answers are the ones the port names: Fetched, Skipped, Refused, Failed.

internal sealed class SettableClock(DateTimeOffset utcNow) : IDateTimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;
}

/// <summary>One message in the scripted mailbox. <see cref="Fetch"/> answers every fetch of it, in every run.</summary>
internal sealed class ScriptedMessage(long position, DateTimeOffset receivedAt, long size)
{
    public long Position { get; } = position;

    public DateTimeOffset ReceivedAt { get; } = receivedAt;

    public long Size { get; } = size;

    public Func<CancellationToken, ValueTask<DmarcMailboxFetch>> Fetch { get; set; } =
        _ => ValueTask.FromResult<DmarcMailboxFetch>(new DmarcMailboxFetch.Skipped(DmarcSkipReason.NoReportPart));
}

/// <summary>A mailbox whose open outcome and content a test sets; every open hands out a new session over the content.</summary>
internal sealed class ScriptedDmarcMailbox : IDmarcMailbox
{
    private readonly SortedDictionary<long, ScriptedMessage> _messages = [];

    public long Epoch { get; set; } = 4_200_017;

    /// <summary>What opening ends in; <see langword="null"/> opens a session.</summary>
    public DmarcMailboxOpen? OpenOutcome { get; set; }

    public Func<CancellationToken, ValueTask>? BeforeOpen { get; set; }

    public DmarcSessionFault? ListingFault { get; set; }

    public int Opens { get; private set; }

    public List<ScriptedDmarcSession> Sessions { get; } = [];

    public List<(long After, int Max)> Listings { get; } = [];

    public List<long> Fetches { get; } = [];

    public IEnumerable<ScriptedMessage> Messages => _messages.Values;

    public ScriptedMessage Add(long position, DateTimeOffset receivedAt, long size = 40_000)
    {
        var message = new ScriptedMessage(position, receivedAt, size);
        _messages.Add(position, message);
        return message;
    }

    public ScriptedMessage this[long position] => _messages[position];

    public async ValueTask<DmarcMailboxOpen> OpenAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Opens++;
        if (BeforeOpen is not null)
        {
            await BeforeOpen(cancellationToken);
        }

        if (OpenOutcome is not null)
        {
            return OpenOutcome;
        }

        var session = new ScriptedDmarcSession(this);
        Sessions.Add(session);
        return new DmarcMailboxOpen.Opened(session);
    }

    internal DmarcMailboxListing List(long position, int max)
    {
        Listings.Add((position, max));
        if (ListingFault is { } fault)
        {
            return new DmarcMailboxListing.Failed(fault);
        }

        return new DmarcMailboxListing.Listed([.. _messages.Values
            .Where(message => message.Position > position)
            .Take(max)
            .Select(message => new DmarcMailboxEntry(message.Position, message.ReceivedAt, message.Size))]);
    }

    internal ValueTask<DmarcMailboxFetch> Fetch(DmarcMailboxEntry entry, CancellationToken cancellationToken)
    {
        Fetches.Add(entry.Position);
        return _messages[entry.Position].Fetch(cancellationToken);
    }
}

internal sealed class ScriptedDmarcSession(ScriptedDmarcMailbox mailbox) : IDmarcMailboxSession
{
    public long Epoch { get; } = mailbox.Epoch;

    public bool Disposed { get; private set; }

    public ValueTask<DmarcMailboxListing> ListAfterAsync(long position, int max, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Disposed, this);
        return ValueTask.FromResult(mailbox.List(position, max));
    }

    public ValueTask<DmarcMailboxFetch> FetchReportAsync(DmarcMailboxEntry entry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Disposed, this);
        return mailbox.Fetch(entry, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

/// <summary>Decodes a part by its first byte, which names the candidate a test registered; anything else is malformed.</summary>
internal sealed class ScriptedDmarcDecoder : IDmarcReportDecoder
{
    private readonly Dictionary<byte, DmarcReportCandidate> _candidates = [];

    public int Decodes { get; private set; }

    public DmarcMailboxFetch.Fetched Part(byte key, DmarcReportCandidate candidate)
    {
        _candidates[key] = candidate;
        return new DmarcMailboxFetch.Fetched([key, 0x50, 0x4B], DmarcReportPartKind.Zip);
    }

    public DmarcDecoding Decode(byte[] content, DmarcReportPartKind kind)
    {
        Decodes++;
        return content.Length > 0 && _candidates.TryGetValue(content[0], out var candidate)
            ? new DmarcDecoding.Decoded(candidate)
            : new DmarcDecoding.Malformed();
    }
}

/// <summary>One ledger row as the store keeps it.</summary>
internal sealed class LedgerRow(DateTimeOffset receivedAt)
{
    public DateTimeOffset ReceivedAt { get; } = receivedAt;

    public DmarcMessageOutcome Outcome { get; set; } = DmarcMessageOutcome.InProgress;

    public int Attempts { get; set; } = 1;

    public int Retries { get; set; }
}

internal sealed record StoredDmarcReport(string Reporter, string ReportId, string Digest, DateOnly ReceivedOn, int Records);

/// <summary>
/// The store's contract in memory: the state row with its cursor, the ledger keyed by epoch and position, the stored
/// reports with the content key and the daily caps. Every call is recorded by name, in order; a write the contract
/// forbids throws. <see cref="FailOn"/> makes a named call throw before it changes anything, as a rolled-back
/// transaction would.
/// </summary>
internal sealed class RecordingDmarcReportStore : IDmarcReportStore
{
    private static readonly HashSet<string> Reads = ["ReadState", "ListRetries"];

    public DmarcRunState? State { get; private set; }

    public Dictionary<(long Epoch, long Position), LedgerRow> Ledger { get; } = [];

    public List<StoredDmarcReport> Reports { get; } = [];

    public List<string> Calls { get; } = [];

    public List<(DateTimeOffset At, DmarcRunOutcome Outcome)> Attempts { get; } = [];

    public List<int> RetryListings { get; } = [];

    public int Analyzes { get; private set; }

    public Func<string, long?, Exception?>? FailOn { get; set; }

    public int Writes => Calls.Count(call => !Reads.Contains(call));

    public LedgerRow Row(long epoch, long position) => Ledger[(epoch, position)];

    public ValueTask<DmarcRunState?> ReadStateAsync(CancellationToken cancellationToken)
    {
        Enter("ReadState", null, cancellationToken);
        return ValueTask.FromResult(State);
    }

    public ValueTask EnsureStateAsync(DateTimeOffset attemptAt, CancellationToken cancellationToken)
    {
        Enter("EnsureState", null, cancellationToken);
        State ??= new DmarcRunState(attemptAt, DmarcRunOutcome.Incomplete, null, null);
        return ValueTask.CompletedTask;
    }

    public ValueTask<DmarcCrashSweep> SweepCrashesAsync(CancellationToken cancellationToken)
    {
        Enter("SweepCrashes", null, cancellationToken);
        int requeued = 0, abandoned = 0;
        foreach (var row in Ledger.Values.Where(row => row.Outcome == DmarcMessageOutcome.InProgress))
        {
            if (row.Attempts >= DmarcIngestionLimits.MaxCrashAttempts)
            {
                row.Outcome = DmarcMessageOutcome.Abandoned;
                abandoned++;
            }
            else
            {
                row.Attempts++;
                row.Outcome = DmarcMessageOutcome.RetryPending;
                requeued++;
            }
        }

        return ValueTask.FromResult(new DmarcCrashSweep(requeued, abandoned));
    }

    public ValueTask<bool> EnterEpochAsync(long epoch, CancellationToken cancellationToken)
    {
        Enter("EnterEpoch", null, cancellationToken);
        var state = State ?? throw new InvalidOperationException("The state row is ensured before the epoch is entered.");
        if (state.Cursor is { } cursor && cursor.Epoch == epoch)
        {
            return ValueTask.FromResult(false);
        }

        if (state.Cursor is { } previous)
        {
            foreach (var key in Ledger.Keys.Where(key => key.Epoch == previous.Epoch).ToList())
            {
                Ledger.Remove(key);
            }
        }

        State = state with { Cursor = new DmarcCursor(epoch, 0) };
        return ValueTask.FromResult(state.Cursor is not null);
    }

    public ValueTask ClaimAsync(long epoch, long position, DateTimeOffset receivedAt, CancellationToken cancellationToken)
    {
        Enter("Claim", position, cancellationToken);
        Advance(epoch, position);
        if (!Ledger.TryAdd((epoch, position), new LedgerRow(receivedAt)))
        {
            throw new InvalidOperationException("A position is claimed once per epoch.");
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask AdvanceCursorAsync(long epoch, long position, CancellationToken cancellationToken)
    {
        Enter("AdvanceCursor", position, cancellationToken);
        Advance(epoch, position);
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<DmarcPendingMessage>> ListRetriesAsync(long epoch, int max, CancellationToken cancellationToken)
    {
        Enter("ListRetries", null, cancellationToken);
        ArgumentOutOfRangeException.ThrowIfLessThan(max, 1);
        RetryListings.Add(max);
        IReadOnlyList<DmarcPendingMessage> pending = [.. Ledger
            .Where(pair => pair.Key.Epoch == epoch && pair.Value.Outcome == DmarcMessageOutcome.RetryPending)
            .OrderBy(pair => pair.Key.Position)
            .Take(max)
            .Select(pair => new DmarcPendingMessage(pair.Key.Position, pair.Value.ReceivedAt, pair.Value.Retries))];
        return ValueTask.FromResult(pending);
    }

    public ValueTask<bool> ClaimRetryAsync(long epoch, long position, CancellationToken cancellationToken)
    {
        Enter("ClaimRetry", position, cancellationToken);
        var row = Ledger[(epoch, position)];
        if (row.Outcome != DmarcMessageOutcome.RetryPending)
        {
            throw new InvalidOperationException("Only a waiting message is claimed for a retry.");
        }

        if (row.Retries >= DmarcIngestionLimits.RetryLimit)
        {
            row.Outcome = DmarcMessageOutcome.Abandoned;
            return ValueTask.FromResult(false);
        }

        row.Retries++;
        row.Outcome = DmarcMessageOutcome.InProgress;
        return ValueTask.FromResult(true);
    }

    public ValueTask SetOutcomeAsync(long epoch, long position, DmarcMessageOutcome outcome, CancellationToken cancellationToken)
    {
        Enter("SetOutcome", position, cancellationToken);
        if (outcome is DmarcMessageOutcome.InProgress or DmarcMessageOutcome.Abandoned or DmarcMessageOutcome.Stored
            or DmarcMessageOutcome.Duplicate or DmarcMessageOutcome.StoredConflicting or DmarcMessageOutcome.VolumeCapped
            or DmarcMessageOutcome.RejectedByStore || !Enum.IsDefined(outcome))
        {
            throw new InvalidOperationException("That outcome is the sweep's, the retry claim's or the store's own.");
        }

        Claimed(epoch, position).Outcome = outcome;
        return ValueTask.CompletedTask;
    }

    public ValueTask<DmarcMessageOutcome> StoreAsync(
        long epoch, long position, DmarcAggregateReport report, CancellationToken cancellationToken)
    {
        Enter("Store", position, cancellationToken);
        var row = Claimed(epoch, position);
        var day = DateOnly.FromDateTime(report.ReceivedAt.UtcDateTime);
        var digest = Convert.ToHexString(report.ContentSha256);
        var ofDay = Reports.Where(stored => stored.ReceivedOn == day).ToList();

        DmarcMessageOutcome outcome;
        if (Reports.Any(stored => stored.Reporter == report.Reporter && stored.ReportId == report.ReportId && stored.Digest == digest))
        {
            outcome = DmarcMessageOutcome.Duplicate;
        }
        else if (ofDay.Count >= DmarcIngestionLimits.MaxStoredReportsPerUtcDay
                 || ofDay.Sum(stored => stored.Records) + report.Records.Count > DmarcIngestionLimits.MaxStoredRecordsPerUtcDay)
        {
            outcome = DmarcMessageOutcome.VolumeCapped;
        }
        else
        {
            outcome = Reports.Any(stored => stored.Reporter == report.Reporter && stored.ReportId == report.ReportId)
                ? DmarcMessageOutcome.StoredConflicting
                : DmarcMessageOutcome.Stored;
            Reports.Add(new StoredDmarcReport(report.Reporter, report.ReportId, digest, day, report.Records.Count));
        }

        row.Outcome = outcome;
        return ValueTask.FromResult(outcome);
    }

    public ValueTask RecordAttemptAsync(DateTimeOffset attemptAt, DmarcRunOutcome outcome, CancellationToken cancellationToken)
    {
        Enter("RecordAttempt", null, cancellationToken);
        if (outcome == DmarcRunOutcome.Incomplete || !Enum.IsDefined(outcome))
        {
            throw new InvalidOperationException("Incomplete is written only by EnsureStateAsync.");
        }

        Attempts.Add((attemptAt, outcome));
        State = new DmarcRunState(
            attemptAt,
            outcome,
            outcome == DmarcRunOutcome.Succeeded ? attemptAt : State?.LastSuccessAt,
            State?.Cursor);
        return ValueTask.CompletedTask;
    }

    public ValueTask AnalyzeAsync(CancellationToken cancellationToken)
    {
        Enter("Analyze", null, cancellationToken);
        Analyzes++;
        return ValueTask.CompletedTask;
    }

    private void Enter(string call, long? position, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls.Add(call);
        if (FailOn?.Invoke(call, position) is { } failure)
        {
            throw failure;
        }
    }

    private void Advance(long epoch, long position)
    {
        var cursor = State?.Cursor ?? throw new InvalidOperationException("The epoch is entered before the cursor moves.");
        if (cursor.Epoch != epoch || position <= cursor.Position)
        {
            throw new InvalidOperationException("The cursor only moves forward, inside its epoch.");
        }

        State = State with { Cursor = new DmarcCursor(epoch, position) };
    }

    private LedgerRow Claimed(long epoch, long position)
    {
        var row = Ledger[(epoch, position)];
        if (row.Outcome != DmarcMessageOutcome.InProgress)
        {
            throw new InvalidOperationException("Only a claimed message's turn is ended.");
        }

        return row;
    }
}

/// <summary>The database failure a store write ends in when Postgres is gone; the job lets it through.</summary>
internal sealed class SimulatedDatabaseFailure : Exception
{
    public SimulatedDatabaseFailure()
        : base("simulated database failure")
    {
    }
}
