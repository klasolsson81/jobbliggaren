namespace Jobbliggaren.Application.Admin.Email;

/// <summary>
/// The allowance region's data as the browser receives it (#1981, ADR 0178). The terms are our own configured
/// facts and are always present; the usage and the plan are closed unions that fail independently, so one failed read
/// never blanks the rest (ADR 0150 D2). Every figure is computed here, and the page and the overview card only format
/// it, so both read one definition. A test pins the serialised key set.
/// </summary>
public sealed record EmailAllowanceDto(EmailAllowanceTermsDto Terms, EmailAllowanceUsageDto Usage, EmailAllowancePlanDto Plan);

/// <summary>
/// The configured terms: the price in <see cref="Currency"/> before VAT, and the rate the estimate is converted at. No URL
/// and no provider string reaches the browser.
/// </summary>
public sealed record EmailAllowanceTermsDto(
    EmailPlan Plan,
    int IncludedMonthlyEmails,
    decimal PricePerThousandExtra,
    string Currency,
    DateOnly PriceReadOn,
    int MonthlySendLimit,
    string CostCurrency,
    decimal SekPerEur,
    DateOnly ExchangeRateOn);

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
        long? estimatedExtraCostOre,
        bool? aboveHalfOfSendLimit,
        int? webhooksCount)
    {
        Status = status;
        Reason = reason;
        ObservedAt = observedAt;
        Month = month;
        Sent = sent;
        Billable = billable;
        EstimatedExtraCostOre = estimatedExtraCostOre;
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
    /// month is named only when UTC and Stockholm agree on it (ADR 0178).
    /// </summary>
    public string? Month { get; }

    /// <summary>The provider's count of mail sent this month in the project.</summary>
    public long? Sent { get; }

    /// <summary>Absent unless the plan in force is the configured one: the terms are that plan's.</summary>
    public long? Billable { get; }

    /// <summary>Computed in kronor with Swedish VAT, in whole öre rounded up; never a billed amount. Absent like <see cref="Billable"/>.</summary>
    public long? EstimatedExtraCostOre { get; }

    /// <summary>ADR 0161 D4 trigger 1.</summary>
    public bool? AboveHalfOfSendLimit { get; }

    /// <summary>Registered webhooks. Absent when the provider's answer held no readable value for it.</summary>
    public int? WebhooksCount { get; }

    public static EmailAllowanceUsageDto Observed(
        DateTimeOffset observedAt,
        string? month,
        long sent,
        long? billable,
        long? estimatedExtraCostOre,
        bool aboveHalfOfSendLimit,
        int? webhooksCount) =>
        (billable is null) != (estimatedExtraCostOre is null)
            ? throw new ArgumentException("Billable and the cost are known together or not at all.", nameof(estimatedExtraCostOre))
            : new(EmailAllowanceStatus.Observed, null, observedAt, month, sent, billable, estimatedExtraCostOre,
                aboveHalfOfSendLimit, webhooksCount);

    public static EmailAllowanceUsageDto NotObserved() =>
        new(EmailAllowanceStatus.NotObserved, EmailAllowanceReason.NotConfigured, null, null, null, null, null, null, null);

    public static EmailAllowanceUsageDto Failed(EmailAllowanceReason reason) =>
        reason == EmailAllowanceReason.NotConfigured
            ? throw new ArgumentException("A failure carries a failure reason.", nameof(reason))
            : new(EmailAllowanceStatus.Failed, reason, null, null, null, null, null, null, null);
}

/// <summary>
/// The plan the provider says the project is on, against the configured one. A cost is computed only when they match;
/// otherwise it is unknown and the page says why.
/// </summary>
public sealed record EmailAllowancePlanDto
{
    private EmailAllowancePlanDto(EmailAllowanceStatus status, EmailAllowanceReason? reason, EmailPlan? active, bool? matches)
    {
        Status = status;
        Reason = reason;
        Active = active;
        Matches = matches;
    }

    public EmailAllowanceStatus Status { get; }

    /// <summary>Present exactly when <see cref="Status"/> is not <see cref="EmailAllowanceStatus.Observed"/>.</summary>
    public EmailAllowanceReason? Reason { get; }

    /// <summary>The plan in force, <see cref="EmailPlan.Unknown"/> when the provider named none this code knows.</summary>
    public EmailPlan? Active { get; }

    /// <summary>Whether <see cref="Active"/> is the configured plan. An unknown plan never matches.</summary>
    public bool? Matches { get; }

    public static EmailAllowancePlanDto Observed(EmailPlan active, bool matches) =>
        new(EmailAllowanceStatus.Observed, null, active, matches);

    public static EmailAllowancePlanDto NotObserved() =>
        new(EmailAllowanceStatus.NotObserved, EmailAllowanceReason.NotConfigured, null, null);

    public static EmailAllowancePlanDto Failed(EmailAllowanceReason reason) =>
        reason == EmailAllowanceReason.NotConfigured
            ? throw new ArgumentException("A failure carries a failure reason.", nameof(reason))
            : new(EmailAllowanceStatus.Failed, reason, null, null);
}
