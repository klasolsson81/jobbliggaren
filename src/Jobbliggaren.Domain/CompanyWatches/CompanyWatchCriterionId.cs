namespace Jobbliggaren.Domain.CompanyWatches;

/// <summary>
/// Strongly-typed id for <see cref="CompanyWatchCriterion"/> (ADR 0011). Parity with
/// <see cref="CompanyWatchId"/> / <c>SavedSearchId</c>.
/// </summary>
public readonly record struct CompanyWatchCriterionId(Guid Value)
{
    public static CompanyWatchCriterionId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
