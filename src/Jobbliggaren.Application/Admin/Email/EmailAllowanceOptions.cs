using System.ComponentModel.DataAnnotations;

namespace Jobbliggaren.Application.Admin.Email;

/// <summary>
/// The provider plan's terms that the allowance figure is computed against (#1981, ADR 0177). Every key has a code
/// default and none is in appsettings: the terms are the same in every deploy, and a default that fails validation
/// fails <c>EmailAllowanceOptionsTests</c> in CI rather than a production boot. Bound by the Api only (section
/// <see cref="SectionName"/>) with <c>ValidateDataAnnotations</c> and <c>ValidateOnStart</c>.
/// <para>
/// The defaults are Scaleway's price list for Transactional Email as read on <see cref="PriceReadOn"/>; the URL and
/// the procedure for re-reading it are in <c>docs/runbooks/admin-email.md</c>. Prices are before VAT, as the price
/// list states them, in <see cref="EmailAllowanceTerms.Currency"/>.
/// </para>
/// </summary>
public sealed class EmailAllowanceOptions : IValidatableObject
{
    public const string SectionName = "EmailAllowance";

    private const decimal MaxPricePerThousand = 1000m;

    public EmailPlan Plan { get; set; } = EmailPlan.Essential;

    /// <summary>The Essential plan's free tier: "300 per organization". The count it is held against is per project (ADR 0177).</summary>
    [Range(0, int.MaxValue)]
    public int IncludedMonthlyEmails { get; set; } = 300;

    /// <summary>Price per 1 000 mails beyond <see cref="IncludedMonthlyEmails"/>.</summary>
    public decimal PricePerThousandExtra { get; set; } = 0.25m;

    /// <summary>The day the price list was read. A computed cost is only as true as this date.</summary>
    public DateOnly PriceReadOn { get; set; } = new(2026, 10, 18);

    /// <summary>
    /// The project's monthly sending limit: Scaleway's documented default (capabilities page validated 2025-10-29,
    /// read 2026-10-11), shared with the sign-in codes. ADR 0161 D4 trigger 1 is held against it.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int MonthlySendLimit { get; set; } = 10_000;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // An enum binds any integer, and Unknown is the adapter's word for a plan it cannot name, never a term.
        if (Plan == EmailPlan.Unknown || !Enum.IsDefined(Plan))
        {
            yield return new ValidationResult("Plan must name a known plan.", [nameof(Plan)]);
        }

        // Checked here rather than with [Range(typeof(decimal), ...)], whose bounds are parsed in the current culture.
        if (PricePerThousandExtra is < 0m or > MaxPricePerThousand)
        {
            yield return new ValidationResult(
                $"PricePerThousandExtra must be between 0 and {MaxPricePerThousand}.", [nameof(PricePerThousandExtra)]);
        }

        // [Required] does not catch default(DateOnly).
        if (PriceReadOn == default)
        {
            yield return new ValidationResult("PriceReadOn must be set.", [nameof(PriceReadOn)]);
        }
    }
}

/// <summary>The terms that are not options, because each has exactly one legal value.</summary>
public static class EmailAllowanceTerms
{
    /// <summary>ISO 4217. Scaleway prices Transactional Email in euros only.</summary>
    public const string Currency = "EUR";
}

/// <summary>How long a provider read is reused (CTO addendum B). The poll bounds one tab; this bounds every viewer.</summary>
public static class EmailAllowanceLimits
{
    /// <summary>A success is served for this long from its own observedAt, never longer.</summary>
    public static readonly TimeSpan ReuseFor = TimeSpan.FromSeconds(60);

    /// <summary>The longest a provider's Retry-After may hold a failure. Longer values are clamped to this.</summary>
    public static readonly TimeSpan RetryAfterCap = TimeSpan.FromMinutes(15);
}
