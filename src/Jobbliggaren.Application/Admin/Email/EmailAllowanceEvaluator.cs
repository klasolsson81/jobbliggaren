using Jobbliggaren.Application.Common.Abstractions;

namespace Jobbliggaren.Application.Admin.Email;

/// <summary>Turns one provider read and the configured terms into <see cref="EmailAllowanceDto"/> (#1981). Pure.</summary>
public static class EmailAllowanceEvaluator
{
    public static EmailAllowanceDto Evaluate(
        EmailProviderUsageRead read, EmailAllowanceOptions options, ISwedishCalendar calendar)
    {
        var terms = new EmailAllowanceTermsDto(
            options.Plan,
            options.IncludedMonthlyEmails,
            options.PricePerThousandExtra,
            EmailAllowanceTerms.Currency,
            options.PriceReadOn,
            options.MonthlySendLimit);

        var usage = read switch
        {
            EmailProviderUsageRead.Observed observed => Observed(observed, options, calendar),
            EmailProviderUsageRead.NotConfigured => EmailAllowanceUsageDto.NotObserved(),
            EmailProviderUsageRead.Failed failed => EmailAllowanceUsageDto.Failed(failed.Reason),
            _ => throw new ArgumentOutOfRangeException(nameof(read), read, "Unknown read."),
        };

        return new EmailAllowanceDto(terms, usage);
    }

    private static EmailAllowanceUsageDto Observed(
        EmailProviderUsageRead.Observed observed, EmailAllowanceOptions options, ISwedishCalendar calendar)
    {
        var sent = observed.MonthlyEmailsCount;
        var billable = EmailAllowanceCalculator.Billable(sent, options.IncludedMonthlyEmails);

        return EmailAllowanceUsageDto.Observed(
            observed.ObservedAt,
            MonthLabel(observed.ObservedAt, calendar),
            sent,
            billable,
            EmailAllowanceCalculator.ExtraCostCents(billable, options.PricePerThousandExtra),
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
