using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.SavedJobAds;
using Jobbliggaren.Domain.SavedJobAds.Events;
using Jobbliggaren.Domain.UnitTests.JobAds;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.SavedJobAds;

// AR — F6 P5 Punkt 2 Del A. Aggregate root med factory <see cref="SavedJobAd.Save"/>.
// Paritet RecentJobSearch — strongly-typed IDs, hard-delete-semantik via Unsave().
public class SavedJobAdTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;
    private static readonly JobSeekerId Seeker = new(Guid.NewGuid());
    private static readonly JobAdId JobAd = new(Guid.NewGuid());

    [Fact]
    public void Save_AssignsAggregateState()
    {
        var saved = SavedJobAd.Save(Seeker, JobAd, Clock.UtcNow);

        saved.Id.Value.ShouldNotBe(Guid.Empty);
        saved.JobSeekerId.ShouldBe(Seeker);
        saved.JobAdId.ShouldBe(JobAd);
        saved.CreatedAt.ShouldBe(Clock.UtcNow);
    }

    [Fact]
    public void Save_RaisesJobAdSavedDomainEvent()
    {
        var saved = SavedJobAd.Save(Seeker, JobAd, Clock.UtcNow);

        var evt = saved.DomainEvents.OfType<JobAdSavedDomainEvent>().ShouldHaveSingleItem();
        evt.SavedJobAdId.ShouldBe(saved.Id);
        evt.JobSeekerId.ShouldBe(Seeker);
        evt.JobAdId.ShouldBe(JobAd);
        evt.OccurredAt.ShouldBe(Clock.UtcNow);
    }

    [Fact]
    public void Save_GeneratesUniqueIds_ForEachInvocation()
    {
        var a = SavedJobAd.Save(Seeker, JobAd, Clock.UtcNow);
        var b = SavedJobAd.Save(Seeker, JobAd, Clock.UtcNow);

        a.Id.ShouldNotBe(b.Id);
    }

    [Fact]
    public void Unsave_RaisesJobAdUnsavedDomainEvent()
    {
        var saved = SavedJobAd.Save(Seeker, JobAd, Clock.UtcNow);
        saved.ClearDomainEvents();
        var unsavedAt = Clock.UtcNow.AddMinutes(5);

        saved.Unsave(unsavedAt);

        var evt = saved.DomainEvents.OfType<JobAdUnsavedDomainEvent>().ShouldHaveSingleItem();
        evt.SavedJobAdId.ShouldBe(saved.Id);
        evt.JobSeekerId.ShouldBe(Seeker);
        evt.JobAdId.ShouldBe(JobAd);
        evt.OccurredAt.ShouldBe(unsavedAt);
    }

    [Fact]
    public void Unsave_DoesNotMutateState()
    {
        var saved = SavedJobAd.Save(Seeker, JobAd, Clock.UtcNow);
        var originalId = saved.Id;
        var originalCreatedAt = saved.CreatedAt;
        var originalJobAdId = saved.JobAdId;

        saved.Unsave(Clock.UtcNow.AddHours(1));

        saved.Id.ShouldBe(originalId);
        saved.CreatedAt.ShouldBe(originalCreatedAt);
        saved.JobAdId.ShouldBe(originalJobAdId);
    }
}
