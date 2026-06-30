namespace Jobbliggaren.Domain.CompanyWatches;

/// <summary>
/// Strongly-typed id for <see cref="CompanyWatch"/> (ADR 0011). Parity with
/// <c>UserJobAdMatchId</c> / <c>SavedSearchId</c>.
/// </summary>
public readonly record struct CompanyWatchId(Guid Value)
{
    public static CompanyWatchId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
