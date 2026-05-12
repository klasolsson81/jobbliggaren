using JobbPilot.Application.Applications.Queries.GetApplicationById;
using JobbPilot.Application.Common.Auditing;
using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.UnitTests.Common;
using JobbPilot.Domain.Applications;
using JobbPilot.Domain.JobSeekers;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.Applications.Queries;

public class GetApplicationByIdQueryHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    public GetApplicationByIdQueryHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private static async Task<(JobSeeker seeker, DomainApplication application)> SeedAsync(
        JobbPilot.Infrastructure.Persistence.AppDbContext db,
        Guid userId,
        string? coverLetter = null)
    {
        var seeker = JobSeeker.Register(userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);

        var app = DomainApplication.Create(seeker.Id, null, coverLetter, FakeDateTimeProvider.Default).Value;
        db.Applications.Add(app);

        await db.SaveChangesAsync(CancellationToken.None);
        return (seeker, app);
    }

    [Fact]
    public async Task Handle_WhenApplicationExists_ReturnsApplicationDetailDto()
    {
        var db = TestAppDbContextFactory.Create();
        var (_, app) = await SeedAsync(db, _userId, "Mitt personliga brev.");

        var handler = new GetApplicationByIdQueryHandler(db, _currentUser, Substitute.For<IFailedAccessLogger>());

        var result = await handler.Handle(new GetApplicationByIdQuery(app.Id.Value), CancellationToken.None);

        result.ShouldNotBeNull();
        result!.Id.ShouldBe(app.Id.Value);
        result.Status.ShouldBe("Draft");
        result.CoverLetter.ShouldBe("Mitt personliga brev.");
    }

    [Fact]
    public async Task Handle_WhenApplicationExists_PopulatesFollowUps()
    {
        var db = TestAppDbContextFactory.Create();
        var (_, app) = await SeedAsync(db, _userId);
        app.AddFollowUp(
            FollowUpChannel.Email,
            FakeDateTimeProvider.Default.UtcNow.AddDays(7),
            "Följ upp",
            FakeDateTimeProvider.Default);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetApplicationByIdQueryHandler(db, _currentUser, Substitute.For<IFailedAccessLogger>());

        var result = await handler.Handle(new GetApplicationByIdQuery(app.Id.Value), CancellationToken.None);

        result.ShouldNotBeNull();
        result!.FollowUps.Count.ShouldBe(1);
        result.FollowUps[0].Channel.ShouldBe("Email");
    }

    [Fact]
    public async Task Handle_WhenApplicationExists_PopulatesNotes()
    {
        var db = TestAppDbContextFactory.Create();
        var (_, app) = await SeedAsync(db, _userId);
        app.AddNote("Bra arbetsgivare.", FakeDateTimeProvider.Default);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetApplicationByIdQueryHandler(db, _currentUser, Substitute.For<IFailedAccessLogger>());

        var result = await handler.Handle(new GetApplicationByIdQuery(app.Id.Value), CancellationToken.None);

        result.ShouldNotBeNull();
        result!.Notes.Count.ShouldBe(1);
        result.Notes[0].Content.ShouldBe("Bra arbetsgivare.");
    }

    [Fact]
    public async Task Handle_WhenApplicationNotFound_ReturnsNull()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(_userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetApplicationByIdQueryHandler(db, _currentUser, Substitute.For<IFailedAccessLogger>());

        var result = await handler.Handle(new GetApplicationByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_WhenApplicationBelongsToOtherUser_ReturnsNull()
    {
        var db = TestAppDbContextFactory.Create();
        var otherUserId = Guid.NewGuid();
        var (_, otherApp) = await SeedAsync(db, otherUserId);

        var handler = new GetApplicationByIdQueryHandler(db, _currentUser, Substitute.For<IFailedAccessLogger>());

        var result = await handler.Handle(new GetApplicationByIdQuery(otherApp.Id.Value), CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_WhenApplicationBelongsToOtherUser_LogsFailedAccessAttempt()
    {
        // TD-67 / ADR 0031: ownership-mismatch loggas via IFailedAccessLogger.
        // Båda users måste ha JobSeeker-rad — annars returnerar handler null
        // via "jobSeekerId == default"-tidig-return innan ownership-checken.
        var db = TestAppDbContextFactory.Create();
        var otherUserId = Guid.NewGuid();
        var (_, otherApp) = await SeedAsync(db, otherUserId);

        var ownSeeker = JobSeeker.Register(_userId, "Current User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(ownSeeker);
        await db.SaveChangesAsync(CancellationToken.None);

        var failedAccessLogger = Substitute.For<IFailedAccessLogger>();
        var handler = new GetApplicationByIdQueryHandler(db, _currentUser, failedAccessLogger);

        await handler.Handle(new GetApplicationByIdQuery(otherApp.Id.Value), CancellationToken.None);

        failedAccessLogger.Received(1).LogCrossUserAttempt(
            "Application",
            otherApp.Id.Value,
            _userId,
            "GetApplicationById");
    }

    [Fact]
    public async Task Handle_WhenApplicationIdUnknown_DoesNotLogFailedAccessAttempt()
    {
        // TD-67 / ADR 0031: okänt id är INTE cross-user-attempt — ska inte logga.
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(_userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(CancellationToken.None);

        var failedAccessLogger = Substitute.For<IFailedAccessLogger>();
        var handler = new GetApplicationByIdQueryHandler(db, _currentUser, failedAccessLogger);

        await handler.Handle(new GetApplicationByIdQuery(Guid.NewGuid()), CancellationToken.None);

        failedAccessLogger.DidNotReceive().LogCrossUserAttempt(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ReturnsNull()
    {
        var db = TestAppDbContextFactory.Create();
        var (_, app) = await SeedAsync(db, _userId);

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);

        var handler = new GetApplicationByIdQueryHandler(db, currentUser, Substitute.For<IFailedAccessLogger>());

        var result = await handler.Handle(new GetApplicationByIdQuery(app.Id.Value), CancellationToken.None);

        result.ShouldBeNull();
    }
}
