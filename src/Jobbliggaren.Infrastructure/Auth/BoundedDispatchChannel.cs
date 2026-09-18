using System.Threading.Channels;

namespace Jobbliggaren.Infrastructure.Auth;

/// <summary>
/// The bounded, dropping, single-reader in-process queue behind every out-of-band auth dispatch (#1171;
/// ADR 0142 D2). Each port gets its OWN instance with its own capacity and its own drop log, so a flood on
/// one flow cannot silently drop the other's work; this base holds only the mechanism they share.
/// </summary>
internal abstract class BoundedDispatchChannel<T>
{
    private readonly Channel<T> _channel;

    protected BoundedDispatchChannel(int capacity)
    {
        Capacity = capacity;

        // DropWrite, never Wait. `Wait` makes the write block once the queue is full, which puts a
        // load-dependent delay back on an unauthenticated endpoint — the same class of channel the
        // dispatch exists to remove, one step sideways, plus a self-inflicted latency DoS.
        // SingleReader: exactly one consumer, so no concurrency ceremony and the channel can use its
        // faster path.
        //
        // The drop is observed through the itemDropped CALLBACK, not through TryWrite's return, and
        // that is a fact about the BCL rather than a preference: under DropWrite, TryWrite returns
        // TRUE even when the queue is full — the write is accepted and the new item is then discarded.
        // A first draft of the password-reset channel read the bool as "queued", and its test caught it.
        // The callback is the only place a drop is visible.
        _channel = Channel.CreateBounded<T>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
                SingleWriter = false,
            },
            itemDropped: _ => OnItemDropped());
    }

    protected int Capacity { get; }

    /// <summary>The consumer's end. Internal — only the hosted service reads it.</summary>
    internal ChannelReader<T> Reader => _channel.Reader;

    /// <summary>Closes the writer so a draining consumer sees the end of the stream on shutdown.</summary>
    internal void Complete() => _channel.Writer.TryComplete();

    /// <summary>
    /// Returns immediately whether the queue is empty or full, and it does so in the same time for an
    /// address that resolves to an account and one that does not, because nothing here looks at the item.
    /// <c>false</c> ONLY once the writer has been completed (shutdown); a FULL queue returns <c>true</c>
    /// and the item is discarded, reported by <see cref="OnItemDropped"/>.
    /// </summary>
    protected bool TryWrite(T item) => _channel.Writer.TryWrite(item);

    /// <summary>
    /// The operator signal for a full queue. Implementations log the capacity and nothing else — never
    /// the dropped item, which the request path wrote before any lookup, so the line stays byte-identical
    /// for an existing and a non-existent account.
    /// </summary>
    protected abstract void OnItemDropped();
}
