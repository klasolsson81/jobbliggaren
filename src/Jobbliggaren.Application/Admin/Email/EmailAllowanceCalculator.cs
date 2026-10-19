namespace Jobbliggaren.Application.Admin.Email;

/// <summary>The arithmetic of the allowance figure (#1981, ADR 0178). Pure; amounts are <see cref="decimal"/>.</summary>
public static class EmailAllowanceCalculator
{
    private const decimal MailsPerPriceUnit = 1000m;
    private const decimal OrePerKrona = 100m;

    /// <summary>The mails sent beyond the included ones.</summary>
    public static long Billable(long sent, int included)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sent);
        ArgumentOutOfRangeException.ThrowIfNegative(included);
        return Math.Max(0, sent - included);
    }

    /// <summary>
    /// The estimated cost of <paramref name="billable"/> mails in whole öre, VAT included: the euro price before VAT,
    /// plus <paramref name="vatRate"/>, at <paramref name="sekPerEur"/>. Rounded UP once, at the end: one extra mail
    /// costs about a third of an öre, and rounding to nearest would show a cost of nothing for the first one.
    /// </summary>
    public static long ExtraCostOre(long billable, decimal pricePerThousand, decimal vatRate, decimal sekPerEur)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(billable);
        ArgumentOutOfRangeException.ThrowIfNegative(pricePerThousand);
        ArgumentOutOfRangeException.ThrowIfNegative(vatRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sekPerEur);
        return (long)decimal.Ceiling(billable * pricePerThousand / MailsPerPriceUnit * (1m + vatRate) * sekPerEur * OrePerKrona);
    }

    /// <summary>
    /// ADR 0161 D4 trigger 1, "monthly sent volume above 50 % of the shared quota", read strictly: exactly half
    /// does not fire it. Also in the docblock of <see cref="EmailAllowanceOptions.MonthlySendLimit"/>.
    /// </summary>
    public static bool IsAboveHalfOfTheSendLimit(long sent, int monthlySendLimit) => sent * 2 > monthlySendLimit;
}
