using JobbPilot.Application.Applications.Queries.GetPipeline;
using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.UnitTests.Common;
using JobbPilot.Domain.Applications;
using JobbPilot.Domain.JobSeekers;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.Applications.Queries;

public class GetPipelineQueryHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    public GetPipelineQueryHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private static async Task<JobSeeker> SeedSeekerAsync(
        JobbPilot.Infrastructure.Persistence.AppDbContext db,
        Guid userId)
    {
        var seeker = JobSeeker.Register(userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(CancellationToken.None);
        return seeker;
    }

    [Fact]
    public async Task Handle_WhenNoApplications_ReturnsEmptyList()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedSeekerAsync(db, _userId);

        var handler = new GetPipelineQueryHandler(db, _currentUser);

        var result = await handler.Handle(new GetPipelineQuery(), CancellationToken.None);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ReturnsEmptyList()
    {
        var db = TestAppDbContextFactory.Create();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);

        var handler = new GetPipelineQueryHandler(db, currentUser);

        var result = await handler.Handle(new GetPipelineQuery(), CancellationToken.None);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_WithApplicationsOfDifferentStatuses_GroupsByStatus()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId);

        var draft1 = DomainApplication.Create(seeker.Id, null, null, FakeDateTimeProvider.Default).Value;
        var draft2 = DomainApplication.Create(seeker.Id, null, null, FakeDateTimeProvider.Default).Value;
        var submitted = DomainApplication.Create(seeker.Id, null, null, FakeDateTimeProvider.Default).Value;
        submitted.TransitionTo(ApplicationStatus.Submitted, FakeDateTimeProvider.Default);

        db.Applications.Add(draft1);
        db.Applications.Add(draft2);
        db.Applications.Add(submitted);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetPipelineQueryHandler(db, _currentUser);

        var result = await handler.Handle(new GetPipelineQuery(), CancellationToken.None);

        result.Count.ShouldBe(2);
        var draftGroup = result.First(g => g.Status == "Draft");
        draftGroup.Count.ShouldBe(2);
        draftGroup.Applications.Count.ShouldBe(2);

        var submittedGroup = result.First(g => g.Status == "Submitted");
        submittedGroup.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_WithSingleApplication_ReturnsSingleGroup()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId);

        var app = DomainApplication.Create(seeker.Id, null, null, FakeDateTimeProvider.Default).Value;
        db.Applications.Add(app);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetPipelineQueryHandler(db, _currentUser);

        var result = await handler.Handle(new GetPipelineQuery(), CancellationToken.None);

        result.Count.ShouldBe(1);
        result[0].Status.ShouldBe("Draft");
        result[0].Count.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_DoesNotReturnApplicationsBelongingToOtherJobSeeker()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId);
        var app = DomainApplication.Create(seeker.Id, null, null, FakeDateTimeProvider.Default).Value;
        db.Applications.Add(app);

        var otherUserId = Guid.NewGuid();
        var otherSeeker = await SeedSeekerAsync(db, otherUserId);
        for (var i = 0; i < 5; i++)
        {
            db.Applications.Add(
                DomainApplication.Create(otherSeeker.Id, null, null, FakeDateTimeProvider.Default).Value);
        }

        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetPipelineQueryHandler(db, _currentUser);

        var result = await handler.Handle(new GetPipelineQuery(), CancellationToken.None);

        result.Sum(g => g.Count).ShouldBe(1);
    }
}
