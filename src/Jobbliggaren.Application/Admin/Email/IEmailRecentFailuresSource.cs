namespace Jobbliggaren.Application.Admin.Email;

/// <summary>
/// The most recent mails the provider could not deliver in a period, each as its time, its masked recipient and a
/// closed reason (#1981 PR 2). Application defines the port; Infrastructure implements it, and the full address never
/// leaves the adapter. The only consumer is the recent-failures query handler, which an architecture test pins.
/// <para>
/// It never throws for a refusal, a timeout or a body it cannot read; those are reasons on the result. The one
/// exception is <see cref="OperationCanceledException"/> when the caller's own token is cancelled.
/// </para>
/// </summary>
public interface IEmailRecentFailuresSource
{
    ValueTask<EmailRecentFailuresRead> ReadAsync(EmailDeliveryPeriod period, CancellationToken cancellationToken);
}

/// <summary>One mail the provider could not deliver. <see cref="CreatedAt"/> is when the mail was created, not when it failed.</summary>
public sealed record EmailFailure
{
    public EmailFailure(DateTimeOffset createdAt, MaskedEmailAddress recipient, EmailFailureReason reason)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "Not a failure reason.");
        }

        CreatedAt = createdAt;
        Recipient = recipient;
        Reason = reason;
    }

    public DateTimeOffset CreatedAt { get; }

    public MaskedEmailAddress Recipient { get; }

    public EmailFailureReason Reason { get; }
}

/// <summary>What one read of the recent failures ends in: a closed set.</summary>
public abstract record EmailRecentFailuresRead
{
    private EmailRecentFailuresRead() { }

    /// <summary>
    /// The provider answered. <see cref="Failures"/> holds at most <see cref="EmailDeliveryLimits.MaxRecentFailures"/>,
    /// newest first, in a copy of its own: the memo shares one answer between every viewer.
    /// </summary>
    public sealed record Observed : EmailRecentFailuresRead
    {
        public Observed(IReadOnlyList<EmailFailure> failures, DateTimeOffset observedAt)
        {
            ArgumentNullException.ThrowIfNull(failures);
            EmailFailure[] copy = [.. failures];
            if (copy.Length > EmailDeliveryLimits.MaxRecentFailures)
            {
                throw new ArgumentException($"At most {EmailDeliveryLimits.MaxRecentFailures} failures.", nameof(failures));
            }

            for (var i = 0; i < copy.Length; i++)
            {
                if (copy[i] is null)
                {
                    throw new ArgumentException("A failure is never null.", nameof(failures));
                }

                if (i > 0 && copy[i].CreatedAt > copy[i - 1].CreatedAt)
                {
                    throw new ArgumentException("Failures are newest first.", nameof(failures));
                }
            }

            Failures = copy;
            ObservedAt = observedAt;
        }

        public IReadOnlyList<EmailFailure> Failures { get; }

        public DateTimeOffset ObservedAt { get; }
    }

    /// <inheritdoc cref="EmailProviderUsageRead.NotConfigured"/>
    public sealed record NotConfigured : EmailRecentFailuresRead;

    /// <inheritdoc cref="EmailProviderPlanRead.ReadKeyMissing"/>
    public sealed record ReadKeyMissing : EmailRecentFailuresRead;

    /// <summary>The read failed for <see cref="Reason"/>, which is never <see cref="EmailProviderReadReason.NotConfigured"/>.</summary>
    public sealed record Failed : EmailRecentFailuresRead
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
