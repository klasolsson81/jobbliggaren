namespace Jobbliggaren.Application.Admin.Email;

/// <summary>
/// The allowance figure's one source (#1981, ADR 0178): the provider's own count of this month's mail, and the plan
/// the project is on, so a cost is computed only against the terms of the plan actually in force. Application defines
/// the port; Infrastructure implements it. The only consumer is <c>GetEmailAllowanceQueryHandler</c>, which an
/// architecture test pins.
/// <para>
/// Neither method throws for a refusal, a timeout or a body it cannot read; those are reasons on the result. The one
/// exception is <see cref="OperationCanceledException"/> when the caller's own token is cancelled. The two reads fail
/// independently.
/// </para>
/// </summary>
public interface IEmailProviderUsageSource
{
    ValueTask<EmailProviderUsageRead> ReadUsageAsync(CancellationToken cancellationToken);

    ValueTask<EmailProviderPlanRead> ReadPlanAsync(CancellationToken cancellationToken);
}

/// <summary>What one read of the project's plan ends in: a closed set.</summary>
public abstract record EmailProviderPlanRead
{
    private EmailProviderPlanRead() { }

    /// <summary>
    /// The provider answered. <paramref name="Plan"/> is <see cref="EmailPlan.Unknown"/> when it named a plan this code
    /// does not know, or not exactly one.
    /// </summary>
    public sealed record Observed(EmailPlan Plan, DateTimeOffset ObservedAt) : EmailProviderPlanRead;

    /// <summary>This deployment sends through no provider that can be asked.</summary>
    public sealed record NotConfigured : EmailProviderPlanRead;

    /// <summary>
    /// The deployment sends through Scaleway, but the read application's key is not configured: an operator step owed,
    /// not a property of the deployment and not a failure. Nothing was read.
    /// </summary>
    public sealed record ReadKeyMissing : EmailProviderPlanRead;

    /// <summary>The read failed for <see cref="Reason"/>, which is never <see cref="EmailProviderReadReason.NotConfigured"/>.</summary>
    public sealed record Failed : EmailProviderPlanRead
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

    /// <inheritdoc cref="EmailProviderPlanRead.ReadKeyMissing"/>
    public sealed record ReadKeyMissing : EmailProviderUsageRead;

    /// <summary>The read failed for <see cref="Reason"/>, which is never <see cref="EmailProviderReadReason.NotConfigured"/>.</summary>
    public sealed record Failed : EmailProviderUsageRead
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
