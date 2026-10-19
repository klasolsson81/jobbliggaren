namespace Jobbliggaren.Application.Admin.Email;

/// <summary>
/// Turns the provider's delivery reads into the browser's closed unions (#1981 PR 2). Pure. It holds the canary: a key
/// that cannot see mail answers zero rather than refusing, so a zero total or an empty list is shown as such only when
/// the sentinel (<see cref="EmailDeliveryLimits.SentinelWindow"/>) counts at least one mail; otherwise the region is
/// <see cref="EmailDeliveryStatus.NotObservable"/>, never 0 (ADR 0150 D2). A non-empty list needs no canary.
/// </summary>
public static class EmailDeliveryEvaluator
{
    public static EmailDeliveryTotalsDto EvaluateTotals(
        EmailDeliveryPeriod period, EmailDeliveryStatisticsRead read, EmailDeliveryStatisticsRead sentinel) =>
        read switch
        {
            EmailDeliveryStatisticsRead.NotConfigured => EmailDeliveryTotalsDto.NotObserved(period),
            EmailDeliveryStatisticsRead.ReadKeyMissing => EmailDeliveryTotalsDto.ReadKeyMissing(period),
            EmailDeliveryStatisticsRead.Failed failed => EmailDeliveryTotalsDto.ReadFailed(period, failed.Reason),
            EmailDeliveryStatisticsRead.Observed observed => sentinel switch
            {
                EmailDeliveryStatisticsRead.Observed { TotalCount: >= 1 } =>
                    EmailDeliveryTotalsDto.Observed(period, observed.ObservedAt, observed.SentCount, observed.FailedCount),
                EmailDeliveryStatisticsRead.Failed failed => EmailDeliveryTotalsDto.ReadFailed(period, failed.Reason),
                EmailDeliveryStatisticsRead.Observed
                    or EmailDeliveryStatisticsRead.NotConfigured
                    or EmailDeliveryStatisticsRead.ReadKeyMissing => EmailDeliveryTotalsDto.NotObservable(period),
                _ => throw new ArgumentOutOfRangeException(nameof(sentinel), sentinel, "Unknown read."),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(read), read, "Unknown read."),
        };

    /// <summary>
    /// The first matching row wins. An empty list is trusted only when the sentinel counts at least one mail and the
    /// same period's statistics are observed with no failure; every other empty list is NotObservable or the failure
    /// that prevented trusting it.
    /// </summary>
    public static EmailRecentFailuresDto EvaluateFailures(
        EmailDeliveryPeriod period,
        EmailRecentFailuresRead failures,
        EmailDeliveryStatisticsRead statistics,
        EmailDeliveryStatisticsRead sentinel) =>
        failures switch
        {
            EmailRecentFailuresRead.NotConfigured => EmailRecentFailuresDto.NotObserved(period),
            EmailRecentFailuresRead.ReadKeyMissing => EmailRecentFailuresDto.ReadKeyMissing(period),
            EmailRecentFailuresRead.Failed failed => EmailRecentFailuresDto.ReadFailed(period, failed.Reason),
            EmailRecentFailuresRead.Observed { Failures.Count: > 0 } observed =>
                EmailRecentFailuresDto.Observed(period, observed.ObservedAt, observed.Failures),
            EmailRecentFailuresRead.Observed empty => EvaluateEmpty(period, empty, statistics, sentinel),
            _ => throw new ArgumentOutOfRangeException(nameof(failures), failures, "Unknown read."),
        };

    private static EmailRecentFailuresDto EvaluateEmpty(
        EmailDeliveryPeriod period,
        EmailRecentFailuresRead.Observed empty,
        EmailDeliveryStatisticsRead statistics,
        EmailDeliveryStatisticsRead sentinel)
    {
        if (sentinel is EmailDeliveryStatisticsRead.Failed sentinelFailed)
        {
            return EmailRecentFailuresDto.ReadFailed(period, sentinelFailed.Reason);
        }

        if (statistics is EmailDeliveryStatisticsRead.Failed statisticsFailed)
        {
            return EmailRecentFailuresDto.ReadFailed(period, statisticsFailed.Reason);
        }

        return sentinel is EmailDeliveryStatisticsRead.Observed { TotalCount: >= 1 }
               && statistics is EmailDeliveryStatisticsRead.Observed { FailedCount: 0 }
            ? EmailRecentFailuresDto.Observed(period, empty.ObservedAt, empty.Failures)
            : EmailRecentFailuresDto.NotObservable(period);
    }
}
