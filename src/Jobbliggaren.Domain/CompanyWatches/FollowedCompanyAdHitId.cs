namespace Jobbliggaren.Domain.CompanyWatches;

/// <summary>
/// Strongly-typed id for <see cref="FollowedCompanyAdHit"/> (ADR 0011). Parity with
/// <c>CompanyWatchId</c> / <c>UserJobAdMatchId</c>.
/// </summary>
public readonly record struct FollowedCompanyAdHitId(Guid Value)
{
    public static FollowedCompanyAdHitId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
