using Jobbliggaren.Application.JobAds.Publication;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobAds;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Publication;

/// <summary>
/// The one publication predicate (ADR 0160), against a frozen clock. The boundary cases are the
/// archiver's: <c>ExpireJobAdsJob</c> archives <c>ExpiresAt &lt; now</c>, so an ad is still listed
/// at <c>ExpiresAt == now</c> and ended one tick later.
/// </summary>
public class JobAdPublicationTests
{
    private static readonly DateTimeOffset Now = FakeDateTimeProvider.Default.UtcNow;
    private static readonly JobSource Imported = JobSource.Platsbanken;

    [Fact]
    public void Classify_ActiveImportedWithoutDeadline_IsListed() =>
        JobAdPublication.Classify(JobAdStatus.Active, null, Imported, Now)
            .ShouldBe(JobAdPublicationState.Listed);

    [Fact]
    public void Classify_DeadlineExactlyNow_IsListed() =>
        JobAdPublication.Classify(JobAdStatus.Active, Now, Imported, Now)
            .ShouldBe(JobAdPublicationState.Listed,
                "ExpireJobAdsJob archives only ExpiresAt < now, so the ad is still live at the instant itself.");

    [Fact]
    public void Classify_DeadlineOneTickAfterNow_IsListed() =>
        JobAdPublication.Classify(JobAdStatus.Active, Now.AddTicks(1), Imported, Now)
            .ShouldBe(JobAdPublicationState.Listed);

    [Fact]
    public void Classify_DeadlineOneTickBeforeNow_IsEnded() =>
        JobAdPublication.Classify(JobAdStatus.Active, Now.AddTicks(-1), Imported, Now)
            .ShouldBe(JobAdPublicationState.Ended,
                "the deadline counts at read time, before the nightly archiver has run.");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Classify_Archived_IsEnded_WhateverTheDeadline(bool withFutureDeadline) =>
        JobAdPublication.Classify(
                JobAdStatus.Archived, withFutureDeadline ? Now.AddDays(10) : null, Imported, Now)
            .ShouldBe(JobAdPublicationState.Ended,
                "an archived ad is not published again by a deadline still in the future.");

    [Fact]
    public void Classify_ErasedImported_IsGone() =>
        JobAdPublication.Classify(JobAdStatus.Erased, null, Imported, Now)
            .ShouldBe(JobAdPublicationState.Gone);

    [Fact]
    public void Classify_ErasedImportedPastDeadline_IsGone_NotEnded() =>
        JobAdPublication.Classify(JobAdStatus.Erased, Now.AddDays(-1), Imported, Now)
            .ShouldBe(JobAdPublicationState.Gone, "erasure is checked before the deadline.");

    [Fact]
    public void Classify_NeverImported_IsNotPublic() =>
        JobAdPublication.Classify(JobAdStatus.Active, null, null, Now)
            .ShouldBe(JobAdPublicationState.NotPublic,
                "a row created through POST /api/v1/job-ads has no External, whatever its Source says.");

    [Fact]
    public void Classify_ImportedFromAnotherSource_IsNotPublic() =>
        JobAdPublication.Classify(JobAdStatus.Active, null, JobSource.LinkedIn, Now)
            .ShouldBe(JobAdPublicationState.NotPublic,
                "only the source whose licence was read is allowlisted.");

    [Theory]
    [InlineData("Active")]
    [InlineData("Archived")]
    [InlineData("Erased")]
    public void Classify_NeverImported_IsNotPublic_InEveryStatus(string status) =>
        JobAdPublication.Classify(JobAdStatus.FromValue(status).Value, Now.AddDays(-1), null, Now)
            .ShouldBe(JobAdPublicationState.NotPublic,
                "NotPublic is decided first, so no state of a row the public surface never held can be "
                + "told apart from an unknown id.");

    [Fact]
    public void PublishedSource_IsPlatsbanken() =>
        JobAdPublication.PublishedSource.ShouldBe(JobSource.Platsbanken,
            "widening the allowlist needs its own licence reading and an ADR 0160 amendment.");
}
