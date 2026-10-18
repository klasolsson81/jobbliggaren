namespace Jobbliggaren.Application.Admin.Email;

/// <summary>The arithmetic of the allowance figure (#1981, ADR 0177). Pure; amounts are <see cref="decimal"/>.</summary>
public static class EmailAllowanceCalculator
{
    private const decimal MailsPerPriceUnit = 1000m;
    private const decimal CentsPerUnit = 100m;

    /// <summary>The mails sent beyond the included ones.</summary>
    public static long Billable(long sent, int included)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sent);
        ArgumentOutOfRangeException.ThrowIfNegative(included);
        return Math.Max(0, sent - included);
    }

    /// <summary>
    /// The estimated cost of <paramref name="billable"/> mails in whole cents, rounded UP: one extra mail costs a
    /// fortieth of a cent, and rounding to nearest would show a cost of nothing for the first twenty of them.
    /// </summary>
    public static long ExtraCostCents(long billable, decimal pricePerThousand)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(billable);
        ArgumentOutOfRangeException.ThrowIfNegative(pricePerThousand);
        return (long)decimal.Ceiling(billable * pricePerThousand / MailsPerPriceUnit * CentsPerUnit);
    }

    /// <summary>
    /// ADR 0161 D4 trigger 1, "monthly sent volume above 50 % of the shared quota", read strictly: exactly half
    /// does not fire it. Also in the docblock of <see cref="EmailAllowanceOptions.MonthlySendLimit"/>.
    /// </summary>
    public static bool IsAboveHalfOfTheSendLimit(long sent, int monthlySendLimit) => sent * 2 > monthlySendLimit;
}
