using System.Text.Json.Serialization;

namespace Jobbliggaren.Application.Admin.Email;

/// <summary>Whether a delivery region has figures to show (#1981 PR 2).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EmailDeliveryStatus>))]
public enum EmailDeliveryStatus
{
    Observed,

    /// <summary>This deployment sends through no provider that can be asked. Carries the reason NotConfigured.</summary>
    NotObserved,

    /// <summary>
    /// The provider answered, but the canary did not pass, so a zero or an empty list cannot be told from a key that
    /// sees no mail. Produced by <see cref="EmailDeliveryEvaluator"/> alone; carries no reason.
    /// </summary>
    NotObservable,

    /// <summary>The deployment sends through Scaleway, but the read key is not configured. Carries no reason.</summary>
    ReadKeyMissing,

    Failed,
}

/// <summary>
/// The delivery totals for one period as the browser receives them: the provider's count of sent and of failed mail.
/// A closed union like the allowance's: figures only when observed, a reason only when not observed or failed, and an
/// unknown figure is absent, never zero. A test pins the serialised key set.
/// </summary>
public sealed record EmailDeliveryTotalsDto
{
    private EmailDeliveryTotalsDto(
        EmailDeliveryPeriod period,
        EmailDeliveryStatus status,
        EmailProviderReadReason? reason,
        DateTimeOffset? observedAt,
        long? sent,
        long? failed)
    {
        Period = period;
        Status = status;
        Reason = reason;
        ObservedAt = observedAt;
        Sent = sent;
        Failed = failed;
    }

    public EmailDeliveryPeriod Period { get; }

    public EmailDeliveryStatus Status { get; }

    /// <summary>Present exactly when <see cref="Status"/> is <see cref="EmailDeliveryStatus.NotObserved"/> or <see cref="EmailDeliveryStatus.Failed"/>.</summary>
    public EmailProviderReadReason? Reason { get; }

    /// <summary>When the provider's answer arrived, which is when we asked: not where the provider's window ends.</summary>
    public DateTimeOffset? ObservedAt { get; }

    /// <summary>Mail the provider counts as sent in the period.</summary>
    public long? Sent { get; }

    /// <summary>Mail the provider counts as failed in the period.</summary>
    public long? Failed { get; }

    public static EmailDeliveryTotalsDto Observed(EmailDeliveryPeriod period, DateTimeOffset observedAt, long sent, long failed)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sent);
        ArgumentOutOfRangeException.ThrowIfNegative(failed);
        return new(period, EmailDeliveryStatus.Observed, null, observedAt, sent, failed);
    }

    public static EmailDeliveryTotalsDto NotObserved(EmailDeliveryPeriod period) =>
        new(period, EmailDeliveryStatus.NotObserved, EmailProviderReadReason.NotConfigured, null, null, null);

    public static EmailDeliveryTotalsDto NotObservable(EmailDeliveryPeriod period) =>
        new(period, EmailDeliveryStatus.NotObservable, null, null, null, null);

    public static EmailDeliveryTotalsDto ReadKeyMissing(EmailDeliveryPeriod period) =>
        new(period, EmailDeliveryStatus.ReadKeyMissing, null, null, null, null);

    public static EmailDeliveryTotalsDto ReadFailed(EmailDeliveryPeriod period, EmailProviderReadReason reason) =>
        reason == EmailProviderReadReason.NotConfigured
            ? throw new ArgumentException("A failure carries a failure reason.", nameof(reason))
            : new(period, EmailDeliveryStatus.Failed, reason, null, null, null);
}

/// <summary>
/// The most recent failed mails in one period as the browser receives them. <see cref="Failures"/> is present only
/// when observed, and an empty list is shown as "no failures" only when the evaluator could trust it.
/// </summary>
public sealed record EmailRecentFailuresDto
{
    private EmailRecentFailuresDto(
        EmailDeliveryPeriod period,
        EmailDeliveryStatus status,
        EmailProviderReadReason? reason,
        DateTimeOffset? observedAt,
        IReadOnlyList<EmailFailureDto>? failures)
    {
        Period = period;
        Status = status;
        Reason = reason;
        ObservedAt = observedAt;
        Failures = failures;
    }

    public EmailDeliveryPeriod Period { get; }

    public EmailDeliveryStatus Status { get; }

    /// <inheritdoc cref="EmailDeliveryTotalsDto.Reason"/>
    public EmailProviderReadReason? Reason { get; }

    /// <inheritdoc cref="EmailDeliveryTotalsDto.ObservedAt"/>
    public DateTimeOffset? ObservedAt { get; }

    /// <summary>Newest first, at most <see cref="Limit"/>. Absent unless observed.</summary>
    public IReadOnlyList<EmailFailureDto>? Failures { get; }

    /// <summary>The most one read returns, so the page can say when it shows only the latest.</summary>
    public int Limit { get; } = EmailDeliveryLimits.MaxRecentFailures;

    public static EmailRecentFailuresDto Observed(
        EmailDeliveryPeriod period, DateTimeOffset observedAt, IReadOnlyList<EmailFailure> failures)
    {
        ArgumentNullException.ThrowIfNull(failures);
        return new(period, EmailDeliveryStatus.Observed, null, observedAt,
            [.. failures.Select(failure => new EmailFailureDto(failure.CreatedAt, failure.Recipient, failure.Reason))]);
    }

    public static EmailRecentFailuresDto NotObserved(EmailDeliveryPeriod period) =>
        new(period, EmailDeliveryStatus.NotObserved, EmailProviderReadReason.NotConfigured, null, null);

    public static EmailRecentFailuresDto NotObservable(EmailDeliveryPeriod period) =>
        new(period, EmailDeliveryStatus.NotObservable, null, null, null);

    public static EmailRecentFailuresDto ReadKeyMissing(EmailDeliveryPeriod period) =>
        new(period, EmailDeliveryStatus.ReadKeyMissing, null, null, null);

    public static EmailRecentFailuresDto ReadFailed(EmailDeliveryPeriod period, EmailProviderReadReason reason) =>
        reason == EmailProviderReadReason.NotConfigured
            ? throw new ArgumentException("A failure carries a failure reason.", nameof(reason))
            : new(period, EmailDeliveryStatus.Failed, reason, null, null);
}

/// <summary>
/// One failed mail on the wire. Its only constructor takes the typed <see cref="MaskedEmailAddress"/>, so a raw address
/// string cannot reach the browser through it.
/// </summary>
public sealed record EmailFailureDto
{
    public EmailFailureDto(DateTimeOffset createdAt, MaskedEmailAddress recipient, EmailFailureReason reason)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        CreatedAt = createdAt;
        Recipient = recipient.Value;
        Reason = reason;
    }

    /// <summary>When the mail was created, not when it failed.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>The masked address, as <see cref="MaskedEmailAddress.Value"/>.</summary>
    public string Recipient { get; }

    public EmailFailureReason Reason { get; }
}
