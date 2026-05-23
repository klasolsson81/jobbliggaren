using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.UnitTests.Common;
using JobbPilot.Application.UserStatus.Queries.GetJobAdStatusBatch;
using JobbPilot.Domain.Common;
using JobbPilot.Domain.JobAds;
using JobbPilot.Domain.JobSeekers;
using JobbPilot.Domain.SavedJobAds;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.UserStatus;

public class GetJobAdStatusBatchQueryHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly FakeDateTimeProvider _clock = FakeDateTimeProvider.Default;
    private readonly Guid _userId = Guid.NewGuid();

    public GetJobAdStatusBatchQueryHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private static JobAd CreateJobAd(FakeDateTimeProvider clock, string title) =>
        JobAd.Create(
            title, Company.Create("Acme").Value, "Desc",
            $"https://example.com/{title}", JobSource.Manual,
            clock.UtcNow, clock.UtcNow.AddDays(30), clock).Value;

    [Fact]
    public async Task Handle_EmptyBatch_ReturnsEmptyDto()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var handler = new GetJobAdStatusBatchQueryHandler(db, _currentUser);

        var result = await handler.Handle(
            new GetJobAdStatusBatchQuery([]), ct);

        result.SavedIds.ShouldBeEmpty();
        result.AppliedIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_AnonymousUser_ReturnsEmptyDto()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var anonUser = Substitute.For<ICurrentUser>();
        anonUser.UserId.Returns((Guid?)null);
        var handler = new GetJobAdStatusBatchQueryHandler(db, anonUser);

        var result = await handler.Handle(
            new GetJobAdStatusBatchQuery([Guid.NewGuid()]), ct);

        result.SavedIds.ShouldBeEmpty();
        result.AppliedIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_ReturnsCorrectSavedAndAppliedIds()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(_userId, "Test", _clock).Value;
        db.JobSeekers.Add(seeker);

        var jobAd1 = CreateJobAd(_clock, "j1");
        var jobAd2 = CreateJobAd(_clock, "j2");
        var jobAd3 = CreateJobAd(_clock, "j3");
        db.JobAds.AddRange(jobAd1, jobAd2, jobAd3);

        // jobAd1 saved, jobAd2 applied, jobAd3 nothing
        db.SavedJobAds.Add(SavedJobAd.Save(seeker.Id, jobAd1.Id, _clock.UtcNow));
        var app = JobbPilot.Domain.Applications.Application
            .Create(seeker.Id, jobAd2.Id, null, null, _clock).Value;
        db.Applications.Add(app);
        await db.SaveChangesAsync(ct);

        var handler = new GetJobAdStatusBatchQueryHandler(db, _currentUser);
        var result = await handler.Handle(
            new GetJobAdStatusBatchQuery([jobAd1.Id.Value, jobAd2.Id.Value, jobAd3.Id.Value]),
            ct);

        result.SavedIds.ShouldContain(jobAd1.Id.Value);
        result.SavedIds.Count.ShouldBe(1);
        result.AppliedIds.ShouldContain(jobAd2.Id.Value);
        result.AppliedIds.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_OtherUserStatus_ExcludedFromResult()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(_userId, "Test", _clock).Value;
        var otherSeeker = JobSeeker.Register(Guid.NewGuid(), "Other", _clock).Value;
        db.JobSeekers.AddRange(seeker, otherSeeker);

        var jobAd = CreateJobAd(_clock, "j1");
        db.JobAds.Add(jobAd);

        // Other user har sparat — ska INTE inkluderas i current users batch
        db.SavedJobAds.Add(SavedJobAd.Save(otherSeeker.Id, jobAd.Id, _clock.UtcNow));
        await db.SaveChangesAsync(ct);

        var handler = new GetJobAdStatusBatchQueryHandler(db, _currentUser);
        var result = await handler.Handle(
            new GetJobAdStatusBatchQuery([jobAd.Id.Value]), ct);

        result.SavedIds.ShouldBeEmpty();
    }
}
