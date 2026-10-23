using Jobbliggaren.Application.JobAds.Queries.GetRelatedPublicJobAds;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Queries.GetRelatedPublicJobAds;

/// <summary>The related section's two bounds (ADR 0182).</summary>
public class RelatedPublicJobAdLimitsTests
{
    [Fact]
    public void MaxRows_IsFive() =>
        RelatedPublicJobAdLimits.MaxRows.ShouldBe(5, "a cap above five is ADR 0166 D10 trigger 17, a decision for that ADR");

    [Fact]
    public void CandidateWindow_HoldsMoreRowsThanTheSectionShows() =>
        RelatedPublicJobAdLimits.CandidateWindow.ShouldBeGreaterThan(RelatedPublicJobAdLimits.MaxRows);
}
