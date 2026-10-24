namespace Jobbliggaren.Application.Admin.Email.Dmarc;

/// <summary>
/// #1981 PR 4a (ADR 0183) — the mailbox the DMARC reports arrive in, read-only. Application defines the port in a
/// neutral vocabulary (an epoch and positions in it); the one implementation lives in the Worker-only mailbox project.
/// Its one consumer is the ingestion job, which an architecture test pins.
/// <para>
/// Nothing here throws for anything the server or a sender can do: a fault is a value. Only the caller's own
/// cancellation propagates.
/// </para>
/// </summary>
public interface IDmarcMailbox
{
    /// <summary>Connects, signs in with one authentication command and opens the mailbox read-only.</summary>
    ValueTask<DmarcMailboxOpen> OpenAsync(CancellationToken cancellationToken);
}

/// <summary>An open, read-only mailbox session. One run holds one session and disposes it.</summary>
public interface IDmarcMailboxSession : IAsyncDisposable
{
    /// <summary>The mailbox's epoch (IMAP UIDVALIDITY). Positions are comparable only within one epoch.</summary>
    long Epoch { get; }

    /// <summary>
    /// At most <paramref name="max"/> messages after <paramref name="position"/>, in position order, found with a
    /// bounded number of commands whatever the mailbox holds. Never lists a position at or below
    /// <paramref name="position"/>.
    /// </summary>
    ValueTask<DmarcMailboxListing> ListAfterAsync(long position, int max, CancellationToken cancellationToken);

    /// <summary>
    /// The one report part of <paramref name="entry"/>, still compressed, chosen from bounded header fetches only.
    /// </summary>
    ValueTask<DmarcMailboxFetch> FetchReportAsync(DmarcMailboxEntry entry, CancellationToken cancellationToken);
}

/// <summary>One listed message: its position, when the mailbox received it, and its size. Nothing a sender wrote.</summary>
public sealed record DmarcMailboxEntry
{
    public DmarcMailboxEntry(long position, DateTimeOffset receivedAt, long size)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(position);
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        Position = position;
        ReceivedAt = receivedAt;
        Size = size;
    }

    public long Position { get; }

    public DateTimeOffset ReceivedAt { get; }

    public long Size { get; }
}

/// <summary>What opening the mailbox ends in: a closed set.</summary>
public abstract record DmarcMailboxOpen
{
    private DmarcMailboxOpen() { }

    public sealed record Opened(IDmarcMailboxSession Session) : DmarcMailboxOpen;

    /// <summary>No password is configured, so the mailbox is not read in this deployment.</summary>
    public sealed record NotConfigured : DmarcMailboxOpen;

    /// <summary>A password pointer is set but the password is blank.</summary>
    public sealed record Misconfigured : DmarcMailboxOpen;

    public sealed record Failed(DmarcSessionFault Fault) : DmarcMailboxOpen;
}

/// <summary>What listing ends in: a closed set.</summary>
public abstract record DmarcMailboxListing
{
    private DmarcMailboxListing() { }

    public sealed record Listed(IReadOnlyList<DmarcMailboxEntry> Entries) : DmarcMailboxListing;

    public sealed record Failed(DmarcSessionFault Fault) : DmarcMailboxListing;
}

/// <summary>What fetching one message's report part ends in: a closed set.</summary>
public abstract record DmarcMailboxFetch
{
    private DmarcMailboxFetch() { }

    /// <summary>The report part, transfer-decoded but still compressed as <paramref name="Kind"/> says.</summary>
    public sealed record Fetched(byte[] Content, DmarcReportPartKind Kind) : DmarcMailboxFetch;

    /// <summary>Nothing in this message may be read; terminal for the message.</summary>
    public sealed record Skipped(DmarcSkipReason Reason) : DmarcMailboxFetch;

    /// <summary>The server refused a command for this message (a tagged NO) and the session is still usable.</summary>
    public sealed record Refused : DmarcMailboxFetch;

    /// <summary>The session can no longer be used; the run ends.</summary>
    public sealed record Failed(DmarcSessionFault Fault) : DmarcMailboxFetch;
}
