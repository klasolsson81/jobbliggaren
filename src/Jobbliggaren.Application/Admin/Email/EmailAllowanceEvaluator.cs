using Jobbliggaren.Application.Common.Abstractions;

namespace Jobbliggaren.Application.Admin.Email;

/// <summary>
/// Turns the provider's two reads and the configured terms into <see cref="EmailAllowanceDto"/> (#1981). Pure. The
/// cost is computed only when the plan in force is the configured one: a cost for a plan that is no longer active
/// would be a fabricated figure (ADR 0150 D2).
/// </summary>
public static class EmailAllowanceEvaluator
{
    public static EmailAllowanceDto Evaluate(
        EmailProviderUsageRead usage,
        EmailProviderPlanRead plan,
        EmailAllowanceOptions options,
        ISwedishCalendar calendar)
    {
        var terms = new EmailAllowanceTermsDto(
            options.Plan,
            options.IncludedMonthlyEmails,
            options.PricePerThousandExtra,
            EmailAllowanceTerms.Currency,
            options.PriceReadOn,
            options.MonthlySendLimit);

        var planDto = plan switch
        {
            EmailProviderPlanRead.Observed observed =>
                EmailAllowancePlanDto.Observed(observed.Plan, observed.Plan != EmailPlan.Unknown && observed.Plan == options.Plan),
            EmailProviderPlanRead.NotConfigured => EmailAllowancePlanDto.NotObserved(),
            EmailProviderPlanRead.Failed failed => EmailAllowancePlanDto.Failed(failed.Reason),
            _ => throw new ArgumentOutOfRangeException(nameof(plan), plan, "Unknown read."),
        };

        var usageDto = usage switch
        {
            EmailProviderUsageRead.Observed observed => Observed(observed, planDto.Matches == true, options, calendar),
            EmailProviderUsageRead.NotConfigured => EmailAllowanceUsageDto.NotObserved(),
            EmailProviderUsageRead.Failed failed => EmailAllowanceUsageDto.Failed(failed.Reason),
            _ => throw new ArgumentOutOfRangeException(nameof(usage), usage, "Unknown read."),
        };

        return new EmailAllowanceDto(terms, usageDto, planDto);
    }

    private static EmailAllowanceUsageDto Observed(
        EmailProviderUsageRead.Observed observed, bool termsApply, EmailAllowanceOptions options, ISwedishCalendar calendar)
    {
        var sent = observed.MonthlyEmailsCount;
        long? billable = termsApply ? EmailAllowanceCalculator.Billable(sent, options.IncludedMonthlyEmails) : null;

        return EmailAllowanceUsageDto.Observed(
            observed.ObservedAt,
            MonthLabel(observed.ObservedAt, calendar),
            sent,
            billable,
            billable is { } extra ? EmailAllowanceCalculator.ExtraCostCents(extra, options.PricePerThousandExtra) : null,
            EmailAllowanceCalculator.IsAboveHalfOfTheSendLimit(sent, options.MonthlySendLimit),
            observed.WebhooksCount);
    }

    /// <summary>
    /// The provider's month resets at an unmeasured instant, UTC or Paris time. Stockholm keeps Paris's offset all
    /// year, so naming the month only when UTC and Stockholm agree is never wrong under either boundary; for the
    /// first hour or two of each month there is no name.
    /// </summary>
    private static string? MonthLabel(DateTimeOffset observedAt, ISwedishCalendar calendar)
    {
        var utc = observedAt.UtcDateTime;
        var stockholm = calendar.MonthOf(observedAt);
        return stockholm == CivilMonth.Of(utc.Year, utc.Month) ? stockholm.ToString() : null;
    }
}
