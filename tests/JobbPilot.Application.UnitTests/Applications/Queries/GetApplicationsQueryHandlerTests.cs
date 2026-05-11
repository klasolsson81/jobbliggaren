using JobbPilot.Application.Applications.Queries.GetApplications;
using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.UnitTests.Common;
using JobbPilot.Domain.Applications;
using JobbPilot.Domain.JobSeekers;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.Applications.Queries;

public class GetApplicationsQueryHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    public GetApplicationsQueryHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private static async Task<(JobSeeker seeker, List<DomainApplication> apps)> SeedAsync(
        JobbPilot.Infrastructure.Persistence.AppDbContext db,
        Guid userId,
        int draftCount = 1,
        int submittedCount = 0)
    {
        var seeker = JobSeeker.Register(userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);

        var apps = new List<DomainApplication>();
        for (var i = 0; i < draftCount; i++)
        {
            var app = DomainApplication.Create(seeker.Id, null, null, FakeDateTimeProvider.Default).Value;
            apps.Add(app);
            db.Applications.Add(app);
        }

        for (var i = 0; i < submittedCount; i++)
        {
            var app = DomainApplication.Create(seeker.Id, null, null, FakeDateTimeProvider.Default).Value;
            app.TransitionTo(ApplicationStatus.Submitted, FakeDateTimeProvider.Default);
            apps.Add(app);
            db.Applications.Add(app);
        }

        await db.SaveChangesAsync(CancellationToken.None);
        return (seeker, apps);
    }

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ReturnsEmptyPagedResult()
    {
        var db = TestAppDbContextFactory.Create();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);

        var handler = new GetApplicationsQueryHandler(db, currentUser);

        var result = await handler.Handle(new GetApplicationsQuery(), CancellationToken.None);

        result.Items.ShouldBeEmpty();
        result.TotalCount.ShouldBe(0);
        result.Page.ShouldBe(1);
        result.PageSize.ShouldBe(20);
    }

    [Fact]
    public async Task Handle_WhenJobSeekerNotFound_ReturnsEmptyPagedResult()
    {
        var db = TestAppDbContextFactory.Create();
        var handler = new GetApplicationsQueryHandler(db, _currentUser);

        var result = await handler.Handle(new GetApplicationsQuery(), CancellationToken.None);

        result.Items.ShouldBeEmpty();
        result.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Handle_WithNoStatusFilter_ReturnsAllApplications()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedAsync(db, _userId, draftCount: 2, submittedCount: 1);

        var handler = new GetApplicationsQueryHandler(db, _currentUser);

        var result = await handler.Handle(new GetApplicationsQuery(), CancellationToken.None);

        result.Items.Count.ShouldBe(3);
        result.TotalCount.ShouldBe(3);
    }

    [Fact]
    public async Task Handle_WithStatusFilter_ReturnsOnlyMatchingApplications()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedAsync(db, _userId, draftCount: 2, submittedCount: 1);

        var handler = new GetApplicationsQueryHandler(db, _currentUser);

        var result = await handler.Handle(new GetApplicationsQuery(Status: "Draft"), CancellationToken.None);

        result.Items.Count.ShouldBe(2);
        result.TotalCount.ShouldBe(2);
        result.Items.ShouldAllBe(a => a.Status == "Draft");
    }

    [Fact]
    public async Task Handle_WithSubmittedStatusFilter_ExcludesDraftApplications()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedAsync(db, _userId, draftCount: 2, submittedCount: 1);

        var handler = new GetApplicationsQueryHandler(db, _currentUser);

        var result = await handler.Handle(new GetApplicationsQuery(Status: "Submitted"), CancellationToken.None);

        result.Items.Count.ShouldBe(1);
        result.TotalCount.ShouldBe(1);
        result.Items.ShouldAllBe(a => a.Status == "Submitted");
    }

    [Fact]
    public async Task Handle_DoesNotReturnApplicationsBelongingToOtherJobSeeker()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedAsync(db, _userId, draftCount: 1);

        var otherUserId = Guid.NewGuid();
        await SeedAsync(db, otherUserId, draftCount: 3);

        var handler = new GetApplicationsQueryHandler(db, _currentUser);

        var result = await handler.Handle(new GetApplicationsQuery(), CancellationToken.None);

        result.Items.Count.ShouldBe(1);
        result.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_TotalCount_IsIndependentOfPageSize()
    {
        // Regression-skydd: TotalCount ska vara total efter filter, inte page-storlek.
        var db = TestAppDbContextFactory.Create();
        await SeedAsync(db, _userId, draftCount: 5);

        var handler = new GetApplicationsQueryHandler(db, _currentUser);

        var result = await handler.Handle(
            new GetApplicationsQuery(PageNumber: 1, PageSize: 2),
            CancellationToken.None);

        result.Items.Count.ShouldBe(2);
        result.TotalCount.ShouldBe(5);
        result.TotalPages.ShouldBe(3);
    }
}
