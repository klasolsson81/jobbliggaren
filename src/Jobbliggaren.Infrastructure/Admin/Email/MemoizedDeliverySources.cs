using Jobbliggaren.Application.Admin.Email;
using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Infrastructure.Admin.Email;

/// <summary>
/// Reuses each statistics read for every viewer, one memo entry per <see cref="StatisticsWindow"/>, with the holds and
/// the single flight <see cref="MemoizedEmailProviderUsageSource"/> describes. The sentinel is its own entry, so the
/// delivery and the failures handlers share it at no extra provider cost. Counts carry no personal data, so nothing is
/// evicted early.
/// </summary>
internal sealed class MemoizedEmailDeliveryStatisticsSource(
    IDeliveryStatisticsAttemptSource inner,
    IDateTimeProvider clock) : IEmailDeliveryStatisticsSource
{
    private readonly KeyedReadMemo<StatisticsWindow, EmailDeliveryStatisticsRead> _memo = new(
        clock, inner.AttemptAsync, read => (read as EmailDeliveryStatisticsRead.Observed)?.ObservedAt);

    public ValueTask<EmailDeliveryStatisticsRead> ReadAsync(EmailDeliveryPeriod period, CancellationToken cancellationToken) =>
        _memo.ReadAsync(StatisticsWindows.Of(period), cancellationToken);

    public ValueTask<EmailDeliveryStatisticsRead> ReadSentinelAsync(CancellationToken cancellationToken) =>
        _memo.ReadAsync(StatisticsWindow.Sentinel, cancellationToken);
}

/// <summary>
/// Reuses each recent-failures read for every viewer, one memo entry per period, with the same holds and single flight.
/// An observed list carries masked recipients, so it is evicted at its reuse deadline: unreferenced by the API
/// <see cref="EmailProviderReadLimits.ReuseFor"/> after the provider's answer, whether or not anyone asks again.
/// </summary>
internal sealed class MemoizedEmailRecentFailuresSource : IEmailRecentFailuresSource
{
    private readonly KeyedReadMemo<EmailDeliveryPeriod, EmailRecentFailuresRead> _memo;

    public MemoizedEmailRecentFailuresSource(IRecentFailuresAttemptSource inner, IDateTimeProvider clock)
        : this(inner, clock, static delay => Task.Delay(delay))
    {
    }

    /// <summary>For tests, which drive the eviction delay. The container considers public constructors only.</summary>
    internal MemoizedEmailRecentFailuresSource(
        IRecentFailuresAttemptSource inner, IDateTimeProvider clock, Func<TimeSpan, Task> evictAfter) =>
        _memo = new(clock, inner.AttemptAsync, read => (read as EmailRecentFailuresRead.Observed)?.ObservedAt, evictAfter);

    public ValueTask<EmailRecentFailuresRead> ReadAsync(EmailDeliveryPeriod period, CancellationToken cancellationToken) =>
        _memo.ReadAsync(period, cancellationToken);
}
