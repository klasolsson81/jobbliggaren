using Jobbliggaren.Application.JobAds.Publication;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Publication;

/// <summary>
/// The floor for an indexable public list (ADR 0166, the Amendment 2026-11-22): the rule's arithmetic at each threshold, on
/// the one expression the query composes. That the database evaluates the same expression is measured on Postgres in
/// <c>JuniorItJobAdQueryReadTests</c>.
/// </summary>
public class PublicListFloorTests
{
    private static readonly Func<EmployerSpread, bool> Holds = PublicListFloor.Holds.Compile();

    [Fact]
    public void TheThresholds_AreSecurityAuditorsNumbers()
    {
        PublicListFloor.MinimumAds.ShouldBe(100);
        PublicListFloor.MinimumEmployers.ShouldBe(25);
    }

    [Theory]
    [InlineData(100, 25, 4, true)]
    [InlineData(99, 25, 4, false)]
    [InlineData(100, 24, 5, false)]
    [InlineData(100, 25, 50, true)]
    [InlineData(100, 25, 51, false)]
    [InlineData(101, 25, 50, true)]
    [InlineData(0, 0, 0, false)]
    public void Holds_AtLeastTheAdsAndTheEmployers_AndNoEmployerAboveHalf(
        int ads, int employers, int largestEmployerAds, bool holds) =>
        Holds(new EmployerSpread { Ads = ads, Employers = employers, LargestEmployerAds = largestEmployerAds })
            .ShouldBe(holds);
}
