using System.Collections.Frozen;
using Jobbliggaren.Application.Admin.Email;
using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Infrastructure.Admin.Email;

/// <summary>
/// One remembered read, with at most one attempt in flight.
/// <para>
/// <b>Eviction is opt-in.</b> Without <c>evictAfter</c> a remembered read stays referenced until the next read of it
/// replaces it. With it, an observed read is dropped at its own reuse deadline, so a read that carries personal data
/// is unreferenced by the API <see cref="EmailProviderReadLimits.ReuseFor"/> after the provider's answer even when no one
/// asks again. A failure is never evicted: it carries no data, and its hold is what keeps a refusing provider unasked.
/// </para>
/// </summary>
internal sealed class ReadMemo<TRead>(
    IDateTimeProvider clock,
    Func<CancellationToken, ValueTask<ProviderAttempt<TRead>>> attempt,
    Func<TRead, DateTimeOffset?> observedAtOf,
    Func<TimeSpan, Task>? evictAfter = null)
{
    private readonly Lock _gate = new();
    private Entry? _entry;
    private Task<TRead>? _inFlight;

    public async ValueTask<TRead> ReadAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource<TRead>? started = null;
        Task<TRead> read;
        lock (_gate)
        {
            if (_entry is { } entry && clock.UtcNow < entry.ReusableUntil)
            {
                return entry.Read;
            }

            if (_inFlight is null)
            {
                started = new TaskCompletionSource<TRead>(TaskCreationOptions.RunContinuationsAsynchronously);
                _inFlight = started.Task;
            }

            read = _inFlight;
        }

        // Started outside the lock, so a read that completes synchronously cannot clear _inFlight before it is set.
        if (started is not null)
        {
            _ = ReadAndRememberAsync(started);
        }

        return await read.WaitAsync(cancellationToken);
    }

    private async Task ReadAndRememberAsync(TaskCompletionSource<TRead> started)
    {
        try
        {
            var result = await attempt(CancellationToken.None);
            var observedAt = observedAtOf(result.Read);
            var entry = new Entry(
                result.Read,
                observedAt is { } at ? at + EmailProviderReadLimits.ReuseFor : clock.UtcNow + FailureHold(result.RetryAfter));

            lock (_gate)
            {
                _entry = entry;
                _inFlight = null;
            }

            if (observedAt is not null && evictAfter is not null)
            {
                _ = EvictWhenDueAsync(entry, evictAfter);
            }

            started.SetResult(result.Read);
        }
        catch (Exception ex)
        {
            // Handed to every waiter, and nothing is remembered.
            lock (_gate)
            {
                _inFlight = null;
            }

            started.SetException(ex);
        }
    }

    /// <summary>
    /// Drops <paramref name="entry"/> at its reuse deadline, unless a newer read has replaced it by then. The drop runs
    /// even if the delay faults, because keeping the entry is the direction that must not happen by accident.
    /// </summary>
    private async Task EvictWhenDueAsync(Entry entry, Func<TimeSpan, Task> delay)
    {
        var due = entry.ReusableUntil - clock.UtcNow;
        try
        {
            await delay(due > TimeSpan.Zero ? due : TimeSpan.Zero);
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_entry, entry))
                {
                    _entry = null;
                }
            }
        }
    }

    private static TimeSpan FailureHold(TimeSpan? retryAfter)
    {
        if (retryAfter is not { } asked || asked <= EmailProviderReadLimits.ReuseFor)
        {
            return EmailProviderReadLimits.ReuseFor;
        }

        return asked < EmailProviderReadLimits.RetryAfterCap ? asked : EmailProviderReadLimits.RetryAfterCap;
    }

    /// <summary>A class, so eviction can tell the entry it scheduled from a newer one by reference.</summary>
    private sealed class Entry(TRead read, DateTimeOffset reusableUntil)
    {
        public TRead Read { get; } = read;

        public DateTimeOffset ReusableUntil { get; } = reusableUntil;
    }
}

/// <summary>
/// One <see cref="ReadMemo{TRead}"/> per key of a closed enum, built eagerly: the key set is fixed at construction, so
/// it never grows and each key has its own single flight and its own hold. An undefined key is refused.
/// </summary>
internal sealed class KeyedReadMemo<TKey, TRead>
    where TKey : struct, Enum
{
    private readonly FrozenDictionary<TKey, ReadMemo<TRead>> _memos;

    public KeyedReadMemo(
        IDateTimeProvider clock,
        Func<TKey, CancellationToken, ValueTask<ProviderAttempt<TRead>>> attempt,
        Func<TRead, DateTimeOffset?> observedAtOf,
        Func<TimeSpan, Task>? evictAfter = null) =>
        _memos = Enum.GetValues<TKey>().Distinct().ToFrozenDictionary(
            key => key,
            key => new ReadMemo<TRead>(clock, cancellationToken => attempt(key, cancellationToken), observedAtOf, evictAfter));

    public ValueTask<TRead> ReadAsync(TKey key, CancellationToken cancellationToken) =>
        _memos.TryGetValue(key, out var memo)
            ? memo.ReadAsync(cancellationToken)
            : throw new ArgumentOutOfRangeException(nameof(key), key, "Not a key of this memo.");
}
