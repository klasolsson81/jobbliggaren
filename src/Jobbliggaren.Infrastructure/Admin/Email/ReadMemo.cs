using Jobbliggaren.Application.Admin.Email;
using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Infrastructure.Admin.Email;

/// <summary>One remembered read, with at most one attempt in flight.</summary>
internal sealed class ReadMemo<TRead>(
    IDateTimeProvider clock,
    Func<CancellationToken, ValueTask<ProviderAttempt<TRead>>> attempt,
    Func<TRead, DateTimeOffset?> observedAtOf)
{
    private readonly Lock _gate = new();
    private (TRead Read, DateTimeOffset ReusableUntil)? _entry;
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
            var reusableUntil = observedAtOf(result.Read) is { } observedAt
                ? observedAt + EmailProviderReadLimits.ReuseFor
                : clock.UtcNow + FailureHold(result.RetryAfter);

            lock (_gate)
            {
                _entry = (result.Read, reusableUntil);
                _inFlight = null;
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

    private static TimeSpan FailureHold(TimeSpan? retryAfter)
    {
        if (retryAfter is not { } asked || asked <= EmailProviderReadLimits.ReuseFor)
        {
            return EmailProviderReadLimits.ReuseFor;
        }

        return asked < EmailProviderReadLimits.RetryAfterCap ? asked : EmailProviderReadLimits.RetryAfterCap;
    }
}
