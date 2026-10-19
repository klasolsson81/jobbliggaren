namespace Jobbliggaren.Application.Admin.Email;

/// <summary>
/// The provider's own delivery counts per period, with no recipient (#1981 PR 2). Application defines the port;
/// Infrastructure implements it. Its consumers are the delivery and the recent-failures query handlers, which an
/// architecture test pins.
/// <para>
/// <see cref="ReadSentinelAsync"/> is the canary: the total over <see cref="EmailDeliveryLimits.SentinelWindow"/>. A key
/// that cannot see mail reads zero for every window, so the evaluator trusts a period's figures only beside a sentinel
/// of at least one. The port carries what the provider said, never whether to trust it.
/// </para>
/// <para>
/// Neither method throws for a refusal, a timeout or a body it cannot read; those are reasons on the result. The one
/// exception is <see cref="OperationCanceledException"/> when the caller's own token is cancelled.
/// </para>
/// </summary>
public interface IEmailDeliveryStatisticsSource
{
    ValueTask<EmailDeliveryStatisticsRead> ReadAsync(EmailDeliveryPeriod period, CancellationToken cancellationToken);

    ValueTask<EmailDeliveryStatisticsRead> ReadSentinelAsync(CancellationToken cancellationToken);
}

/// <summary>What one read of the provider's delivery counts ends in: a closed set.</summary>
public abstract record EmailDeliveryStatisticsRead
{
    private EmailDeliveryStatisticsRead() { }

    /// <summary>
    /// The provider answered. The counts cover the mail created in the window; sent and failed are part of the total,
    /// which also holds mail still queued or cancelled. <paramref name="observedAt"/> is when the answer arrived.
    /// </summary>
    public sealed record Observed : EmailDeliveryStatisticsRead
    {
        public Observed(long total, long sent, long failed, DateTimeOffset observedAt)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(total);
            ArgumentOutOfRangeException.ThrowIfNegative(sent);
            ArgumentOutOfRangeException.ThrowIfNegative(failed);
            if (sent + failed > total)
            {
                throw new ArgumentException("Sent and failed are part of the total.", nameof(failed));
            }

            TotalCount = total;
            SentCount = sent;
            FailedCount = failed;
            ObservedAt = observedAt;
        }

        public long TotalCount { get; }

        public long SentCount { get; }

        public long FailedCount { get; }

        public DateTimeOffset ObservedAt { get; }
    }

    /// <inheritdoc cref="EmailProviderUsageRead.NotConfigured"/>
    public sealed record NotConfigured : EmailDeliveryStatisticsRead;

    /// <inheritdoc cref="EmailProviderPlanRead.ReadKeyMissing"/>
    public sealed record ReadKeyMissing : EmailDeliveryStatisticsRead;

    /// <summary>The read failed for <see cref="Reason"/>, which is never <see cref="EmailProviderReadReason.NotConfigured"/>.</summary>
    public sealed record Failed : EmailDeliveryStatisticsRead
    {
        public Failed(EmailProviderReadReason reason)
        {
            if (reason == EmailProviderReadReason.NotConfigured || !Enum.IsDefined(reason))
            {
                throw new ArgumentException("A failure carries a failure reason.", nameof(reason));
            }

            Reason = reason;
        }

        public EmailProviderReadReason Reason { get; }
    }
}
