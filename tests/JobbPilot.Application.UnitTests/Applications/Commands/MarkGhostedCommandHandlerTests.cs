using JobbPilot.Application.Applications.Commands.MarkGhosted;
using JobbPilot.Application.UnitTests.Common;
using JobbPilot.Domain.Applications;
using JobbPilot.Domain.JobSeekers;
using Shouldly;

namespace JobbPilot.Application.UnitTests.Applications.Commands;

public class MarkGhostedCommandHandlerTests
{
    private static async Task<(JobSeeker seeker, DomainApplication application)> SeedWithStatusAsync(
        JobbPilot.Infrastructure.Persistence.AppDbContext db,
        ApplicationStatus targetStatus)
    {
        var seeker = JobSeeker.Register(Guid.NewGuid(), "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);

        var app = DomainApplication.Create(seeker.Id, null, null, FakeDateTimeProvider.Default).Value;

        if (targetStatus == ApplicationStatus.Submitted || targetStatus == ApplicationStatus.Acknowledged)
        {
            app.TransitionTo(ApplicationStatus.Submitted, FakeDateTimeProvider.Default);
        }

        if (targetStatus == ApplicationStatus.Acknowledged)
        {
            app.TransitionTo(ApplicationStatus.Acknowledged, FakeDateTimeProvider.Default);
        }

        db.Applications.Add(app);
        await db.SaveChangesAsync(CancellationToken.None);
        return (seeker, app);
    }

    [Fact]
    public async Task Handle_WhenSubmitted_TransitionsToGhostedAndReturnsSuccess()
    {
        var db = TestAppDbContextFactory.Create();
        var (_, app) = await SeedWithStatusAsync(db, ApplicationStatus.Submitted);

        var handler = new MarkGhostedCommandHandler(db, FakeDateTimeProvider.Default);
        var command = new MarkGhostedCommand(app.Id.Value);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var updated = await db.Applications.FindAsync([app.Id], TestContext.Current.CancellationToken);
        updated!.Status.ShouldBe(ApplicationStatus.Ghosted);
    }

    [Fact]
    public async Task Handle_WhenDraft_IsNoOpAndReturnsSuccess()
    {
        var db = TestAppDbContextFactory.Create();
        var (_, app) = await SeedWithStatusAsync(db, ApplicationStatus.Draft);

        var handler = new MarkGhostedCommandHandler(db, FakeDateTimeProvider.Default);
        var command = new MarkGhostedCommand(app.Id.Value);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var updated = await db.Applications.FindAsync([app.Id], TestContext.Current.CancellationToken);
        updated!.Status.ShouldBe(ApplicationStatus.Draft);
    }

    [Fact]
    public async Task Handle_WhenApplicationNotFound_ReturnsSuccess()
    {
        var db = TestAppDbContextFactory.Create();

        var handler = new MarkGhostedCommandHandler(db, FakeDateTimeProvider.Default);
        var command = new MarkGhostedCommand(Guid.NewGuid());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
    }
}
