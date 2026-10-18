namespace Jobbliggaren.Application.Admin.Email;

/// <summary>
/// The allowance region's data as the browser receives it (#1981, ADR 0177). The terms are our own configured
/// facts and are always present; the usage is a closed union, so a failed read never blanks the terms (ADR 0150 D2).
/// Every figure is computed here, and the page and the overview card only format it, so both read one definition.
/// A test pins the serialised key set.
/// </summary>
public sealed record EmailAllowanceDto(EmailAllowanceTermsDto Terms, EmailAllowanceUsageDto Usage);

/// <summary>The configured terms. No URL and no provider string reaches the browser.</summary>
public sealed record EmailAllowanceTermsDto(
    EmailPlan Plan,
    int IncludedMonthlyEmails,
    decimal PricePerThousandExtra,
    string Currency,
    DateOnly PriceReadOn,
    int MonthlySendLimit);

/// <summary>
/// This month's usage, as a closed union: an observed reading carries its figures, a reading that is not observed
/// or failed carries a reason and nothing else. An unknown figure is absent, never zero.
/// </summary>
public sealed record EmailAllowanceUsageDto
{
    private EmailAllowanceUsageDto(
        EmailAllowanceStatus status,
        EmailAllowanceReason? reason,
        DateTimeOffset? observedAt,
        string? month,
        long? sent,
        long? billable,
        long? estimatedExtraCostCents,
        bool? aboveHalfOfSendLimit,
        int? webhooksCount)
    {
        Status = status;
        Reason = reason;
        ObservedAt = observedAt;
        Month = month;
        Sent = sent;
        Billable = billable;
        EstimatedExtraCostCents = estimatedExtraCostCents;
        AboveHalfOfSendLimit = aboveHalfOfSendLimit;
        WebhooksCount = webhooksCount;
    }

    public EmailAllowanceStatus Status { get; }

    /// <summary>Present exactly when <see cref="Status"/> is not <see cref="EmailAllowanceStatus.Observed"/>.</summary>
    public EmailAllowanceReason? Reason { get; }

    /// <summary>When the provider's answer arrived; a reused answer keeps its own instant.</summary>
    public DateTimeOffset? ObservedAt { get; }

    /// <summary>
    /// <c>YYYY-MM</c>, or absent where the provider's month cannot be named: its reset instant is unmeasured, so a
    /// month is named only when UTC and Stockholm agree on it (ADR 0177).
    /// </summary>
    public string? Month { get; }

    /// <summary>The provider's count of mail sent this month in the project.</summary>
    public long? Sent { get; }

    public long? Billable { get; }

    /// <summary>Computed, rounded up to the whole cent, before VAT. Never a billed amount.</summary>
    public long? EstimatedExtraCostCents { get; }

    /// <summary>ADR 0161 D4 trigger 1.</summary>
    public bool? AboveHalfOfSendLimit { get; }

    /// <summary>Registered webhooks. Absent when the provider's answer held no readable value for it.</summary>
    public int? WebhooksCount { get; }

    public static EmailAllowanceUsageDto Observed(
        DateTimeOffset observedAt,
        string? month,
        long sent,
        long billable,
        long estimatedExtraCostCents,
        bool aboveHalfOfSendLimit,
        int? webhooksCount) =>
        new(EmailAllowanceStatus.Observed, null, observedAt, month, sent, billable, estimatedExtraCostCents,
            aboveHalfOfSendLimit, webhooksCount);

    public static EmailAllowanceUsageDto NotObserved() =>
        new(EmailAllowanceStatus.NotObserved, EmailAllowanceReason.NotConfigured, null, null, null, null, null, null, null);

    public static EmailAllowanceUsageDto Failed(EmailAllowanceReason reason) =>
        reason == EmailAllowanceReason.NotConfigured
            ? throw new ArgumentException("A failure carries a failure reason.", nameof(reason))
            : new(EmailAllowanceStatus.Failed, reason, null, null, null, null, null, null, null);
}
