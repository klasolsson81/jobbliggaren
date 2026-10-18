using Jobbliggaren.Application.JobAds.Listing;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Listing;

/// <summary>
/// How the snapshot's bounds relate (ADR 0174). The age bound is part of ADR 0166's record (D10 trigger 9): a list
/// answer older than 15 minutes is a new look at the legal conclusion, so the bound is pinned here, not chosen per
/// deployment.
/// </summary>
public class ListedJobAdSnapshotLimitsTests
{
    [Fact]
    public void MaxAge_IsAtMostFifteenMinutes() =>
        ListedJobAdSnapshotLimits.MaxAge.ShouldBeLessThanOrEqualTo(TimeSpan.FromMinutes(15),
            "ADR 0166 D10 trigger 9: a list older than 15 minutes fires the trigger.");

    [Fact]
    public void MaxAge_SpansMoreThanTwoIntervals_SoOneFailedReadDoesNotTakeTheSitemapDown() =>
        ListedJobAdSnapshotLimits.MaxAge.ShouldBeGreaterThan(2 * ListedJobAdSnapshotLimits.SampleInterval);

    [Fact]
    public void FutureTolerance_IsShorterThanOneInterval() =>
        ListedJobAdSnapshotLimits.FutureTolerance.ShouldBeLessThan(ListedJobAdSnapshotLimits.SampleInterval);

    [Fact]
    public void PartCount_DividesTheHexDigitsEvenly() =>
        ListedJobAdPartition.IsSupported(ListedJobAdSnapshotLimits.PartCount).ShouldBeTrue();

    [Fact]
    public void Ceiling_LeavesEachPartBelowTheFiftyThousandUrlsOfASitemapFile() =>
        (ListedJobAdSnapshotLimits.Ceiling / ListedJobAdSnapshotLimits.PartCount).ShouldBeLessThanOrEqualTo(45_000,
            "the API must refuse at its ceiling before a part could reach the per-file cap; the ids spread evenly, so "
            + "a part at the ceiling sits a few hundred ids either side of Ceiling / PartCount.");
}
