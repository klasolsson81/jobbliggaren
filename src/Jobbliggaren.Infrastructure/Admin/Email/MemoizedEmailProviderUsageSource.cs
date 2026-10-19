using Jobbliggaren.Application.Admin.Email;
using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Infrastructure.Admin.Email;

/// <summary>
/// Reuses each provider read for every viewer, and lets only one of each be in flight (#1981, ADR 0178). The count
/// and the plan are remembered separately, so one failing never blanks the other.
/// <list type="bullet">
///   <item>A success is reused until <see cref="EmailProviderReadLimits.ReuseFor"/> after its own observedAt, and never
///     after: keeping a last good value is the web's concern.</item>
///   <item>A failure is reused too, for <see cref="EmailProviderReadLimits.ReuseFor"/> or the provider's
///     <c>Retry-After</c> when that is longer, clamped to <see cref="EmailProviderReadLimits.RetryAfterCap"/>. The send
///     path has no retry, so this read must not add load to a provider that is already refusing.</item>
///   <item>The shared read runs on <see cref="CancellationToken.None"/>, bounded by the client's timeout, and each
///     caller waits with its own token. A tab that is hidden or closed gives up its wait, never the read the other
///     viewers are waiting on.</item>
/// </list>
/// A read that throws, which the adapter's contract rules out, is not reused, and the next caller reads again.
/// </summary>
internal sealed class MemoizedEmailProviderUsageSource(
    IProviderUsageAttemptSource inner,
    IDateTimeProvider clock) : IEmailProviderUsageSource
{
    private readonly ReadMemo<EmailProviderUsageRead> _usage = new(
        clock, inner.AttemptUsageAsync, read => (read as EmailProviderUsageRead.Observed)?.ObservedAt);

    private readonly ReadMemo<EmailProviderPlanRead> _plan = new(
        clock, inner.AttemptPlanAsync, read => (read as EmailProviderPlanRead.Observed)?.ObservedAt);

    public ValueTask<EmailProviderUsageRead> ReadUsageAsync(CancellationToken cancellationToken) =>
        _usage.ReadAsync(cancellationToken);

    public ValueTask<EmailProviderPlanRead> ReadPlanAsync(CancellationToken cancellationToken) =>
        _plan.ReadAsync(cancellationToken);

    /// <summary>One remembered read, with at most one attempt in flight.</summary>
    private sealed class ReadMemo<TRead>(
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
}
