namespace Jobbliggaren.Application.Admin.Email;

/// <summary>
/// The allowance figure's one source: the provider's own count of this month's mail (#1981, ADR 0177). Application
/// defines the port; Infrastructure implements it. The only consumer is <c>GetEmailAllowanceQueryHandler</c>, which
/// an architecture test pins.
/// </summary>
public interface IEmailProviderUsageSource
{
    /// <summary>
    /// Never throws for a refusal, a timeout or a body it cannot read; those are reasons on the result. The one
    /// exception is <see cref="OperationCanceledException"/> when the caller's own token is cancelled.
    /// </summary>
    ValueTask<EmailProviderUsageRead> ReadUsageAsync(CancellationToken cancellationToken);
}

/// <summary>What one read of the provider ends in: a closed set.</summary>
public abstract record EmailProviderUsageRead
{
    private EmailProviderUsageRead() { }

    /// <summary>
    /// The provider answered. <paramref name="WebhooksCount"/> is null when that one field could not be read; the
    /// count stands regardless. <paramref name="ObservedAt"/> is the instant the provider's answer arrived.
    /// </summary>
    public sealed record Observed : EmailProviderUsageRead
    {
        public Observed(long monthlyEmailsCount, int? webhooksCount, DateTimeOffset observedAt)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(monthlyEmailsCount);
            if (webhooksCount is { } webhooks)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(webhooks, nameof(webhooksCount));
            }

            MonthlyEmailsCount = monthlyEmailsCount;
            WebhooksCount = webhooksCount;
            ObservedAt = observedAt;
        }

        public long MonthlyEmailsCount { get; }

        public int? WebhooksCount { get; }

        public DateTimeOffset ObservedAt { get; }
    }

    /// <summary>This deployment sends through no provider that can be asked.</summary>
    public sealed record NotConfigured : EmailProviderUsageRead;

    /// <summary>The read failed for <see cref="Reason"/>, which is never <see cref="EmailAllowanceReason.NotConfigured"/>.</summary>
    public sealed record Failed : EmailProviderUsageRead
    {
        public Failed(EmailAllowanceReason reason)
        {
            if (reason == EmailAllowanceReason.NotConfigured || !Enum.IsDefined(reason))
            {
                throw new ArgumentException("A failure carries a failure reason.", nameof(reason));
            }

            Reason = reason;
        }

        public EmailAllowanceReason Reason { get; }
    }
}
