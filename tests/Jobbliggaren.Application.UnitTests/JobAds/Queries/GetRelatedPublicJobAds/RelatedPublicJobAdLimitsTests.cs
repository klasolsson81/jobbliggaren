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
    public void CandidateWindow_Is100() =>
        RelatedPublicJobAdLimits.CandidateWindow.ShouldBe(100,
            "the window is set by cost (ADR 0182 D2): the database's work does not depend on it, and it decides how often the "
            + "section comes up short of five");

    [Fact]
    public void CandidateWindow_HoldsMoreRowsThanTheSectionShows() =>
        RelatedPublicJobAdLimits.CandidateWindow.ShouldBeGreaterThan(RelatedPublicJobAdLimits.MaxRows);
}
