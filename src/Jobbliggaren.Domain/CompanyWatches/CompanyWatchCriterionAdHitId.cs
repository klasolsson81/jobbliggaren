namespace Jobbliggaren.Domain.CompanyWatches;

/// <summary>
/// Strongly-typed id for <see cref="CompanyWatchCriterionAdHit"/> (ADR 0011). Parity with
/// <see cref="FollowedCompanyAdHitId"/>.
/// </summary>
public readonly record struct CompanyWatchCriterionAdHitId(Guid Value)
{
    public static CompanyWatchCriterionAdHitId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
