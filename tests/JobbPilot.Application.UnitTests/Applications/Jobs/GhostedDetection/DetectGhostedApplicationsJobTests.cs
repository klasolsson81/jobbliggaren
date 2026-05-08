using JobbPilot.Application.Applications.Commands.MarkGhosted;
using JobbPilot.Application.Applications.Jobs.GhostedDetection;
using JobbPilot.Application.UnitTests.Common;
using JobbPilot.Domain.Applications;
using JobbPilot.Domain.JobSeekers;
using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.Applications.Jobs.GhostedDetection;

public class DetectGhostedApplicationsJobTests
{
    private static readonly FakeDateTimeProvider NowClock =
        new(new DateTimeOffset(2026, 5, 8, 12, 0, 0, TimeSpan.Zero));

    private static FakeDateTimeProvider ClockAt(int year, int month, int day) =>
        new(new DateTimeOffset(year, month, day, 12, 0, 0, TimeSpan.Zero));

    private static async Task<DomainApplication> SeedApplicationAsync(
        JobbPilot.Infrastructure.Persistence.AppDbContext db,
        ApplicationStatus targetStatus,
        FakeDateTimeProvider statusChangeClock,
        int? overrideThresholdDays = null,
        bool softDeleted = false)
    {
        var seeker = JobSeeker.Register(Guid.NewGuid(), "Test User", statusChangeClock).Value;
        db.JobSeekers.Add(seeker);

        var app = DomainApplication.Create(seeker.Id, null, null, statusChangeClock).Value;

        if (targetStatus == ApplicationStatus.Submitted ||
            targetStatus == ApplicationStatus.Acknowledged ||
            targetStatus == ApplicationStatus.Rejected ||
            targetStatus == ApplicationStatus.Withdrawn)
        {
            app.TransitionTo(ApplicationStatus.Submitted, statusChangeClock);
        }

        if (targetStatus == ApplicationStatus.Acknowledged)
        {
            app.TransitionTo(ApplicationStatus.Acknowledged, statusChangeClock);
        }
        else if (targetStatus == ApplicationStatus.Rejected)
        {
            app.TransitionTo(ApplicationStatus.Rejected, statusChangeClock);
        }
        else if (targetStatus == ApplicationStatus.Withdrawn)
        {
            app.TransitionTo(ApplicationStatus.Withdrawn, statusChangeClock);
        }
        else if (targetStatus == ApplicationStatus.Ghosted)
        {
            app.MarkGhosted(statusChangeClock);
        }

        if (softDeleted)
            app.SoftDelete(statusChangeClock);

        db.Applications.Add(app);

        if (overrideThresholdDays is not null)
        {
            db.Entry(app).Property(nameof(DomainApplication.GhostedThresholdDays)).CurrentValue =
                overrideThresholdDays.Value;
        }

        await db.SaveChangesAsync(CancellationToken.None);
        return app;
    }

    private static DetectGhostedApplicationsJob CreateJob(
        JobbPilot.Infrastructure.Persistence.AppDbContext db,
        IMediator mediator,
        FakeDateTimeProvider clock) =>
        new(db, mediator, clock, NullLogger<DetectGhostedApplicationsJob>.Instance);

    [Fact]
    public async Task RunAsync_NoApplications_DoesNotSendAnyCommand()
    {
        var db = TestAppDbContextFactory.Create();
        var mediator = Substitute.For<IMediator>();

        var job = CreateJob(db, mediator, NowClock);
        await job.RunAsync(CancellationToken.None);

        await mediator.DidNotReceiveWithAnyArgs().Send(Arg.Any<MarkGhostedCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_StaleSubmittedApplication_SendsMarkGhostedCommand()
    {
        var db = TestAppDbContextFactory.Create();
        var pastClock = ClockAt(2026, 4, 1); // 37 dagar < 21-dagars-threshold
        var app = await SeedApplicationAsync(db, ApplicationStatus.Submitted, pastClock);
        var mediator = Substitute.For<IMediator>();

        var job = CreateJob(db, mediator, NowClock);
        await job.RunAsync(CancellationToken.None);

        await mediator.Received(1).Send(
            Arg.Is<MarkGhostedCommand>(c => c.ApplicationId == app.Id.Value),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_StaleAcknowledgedApplication_SendsMarkGhostedCommand()
    {
        var db = TestAppDbContextFactory.Create();
        var pastClock = ClockAt(2026, 4, 1);
        var app = await SeedApplicationAsync(db, ApplicationStatus.Acknowledged, pastClock);
        var mediator = Substitute.For<IMediator>();

        var job = CreateJob(db, mediator, NowClock);
        await job.RunAsync(CancellationToken.None);

        await mediator.Received(1).Send(
            Arg.Is<MarkGhostedCommand>(c => c.ApplicationId == app.Id.Value),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_DraftApplication_DoesNotSendCommand()
    {
        var db = TestAppDbContextFactory.Create();
        var pastClock = ClockAt(2026, 4, 1);
        await SeedApplicationAsync(db, ApplicationStatus.Draft, pastClock);
        var mediator = Substitute.For<IMediator>();

        var job = CreateJob(db, mediator, NowClock);
        await job.RunAsync(CancellationToken.None);

        await mediator.DidNotReceiveWithAnyArgs().Send(Arg.Any<MarkGhostedCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_RejectedApplication_DoesNotSendCommand()
    {
        var db = TestAppDbContextFactory.Create();
        var pastClock = ClockAt(2026, 4, 1);
        await SeedApplicationAsync(db, ApplicationStatus.Rejected, pastClock);
        var mediator = Substitute.For<IMediator>();

        var job = CreateJob(db, mediator, NowClock);
        await job.RunAsync(CancellationToken.None);

        await mediator.DidNotReceiveWithAnyArgs().Send(Arg.Any<MarkGhostedCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_AlreadyGhostedApplication_DoesNotSendCommand()
    {
        var db = TestAppDbContextFactory.Create();
        var pastClock = ClockAt(2026, 4, 1);
        await SeedApplicationAsync(db, ApplicationStatus.Ghosted, pastClock);
        var mediator = Substitute.For<IMediator>();

        var job = CreateJob(db, mediator, NowClock);
        await job.RunAsync(CancellationToken.None);

        await mediator.DidNotReceiveWithAnyArgs().Send(Arg.Any<MarkGhostedCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_RecentSubmittedApplication_DoesNotSendCommand()
    {
        var db = TestAppDbContextFactory.Create();
        // 5 dagar bakåt — väl inom 21-dagars default-threshold
        var recentClock = ClockAt(2026, 5, 3);
        await SeedApplicationAsync(db, ApplicationStatus.Submitted, recentClock);
        var mediator = Substitute.For<IMediator>();

        var job = CreateJob(db, mediator, NowClock);
        await job.RunAsync(CancellationToken.None);

        await mediator.DidNotReceiveWithAnyArgs().Send(Arg.Any<MarkGhostedCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_SoftDeletedApplication_DoesNotSendCommand()
    {
        var db = TestAppDbContextFactory.Create();
        var pastClock = ClockAt(2026, 4, 1);
        await SeedApplicationAsync(db, ApplicationStatus.Submitted, pastClock, softDeleted: true);
        var mediator = Substitute.For<IMediator>();

        var job = CreateJob(db, mediator, NowClock);
        await job.RunAsync(CancellationToken.None);

        await mediator.DidNotReceiveWithAnyArgs().Send(Arg.Any<MarkGhostedCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_RespectsPerApplicationThresholdShorterThanDefault()
    {
        var db = TestAppDbContextFactory.Create();
        // 8 dagar bakåt — INOM 21-dagars default men ÖVER en 5-dagars-override
        var clockEightDaysAgo = ClockAt(2026, 4, 30);
        var app = await SeedApplicationAsync(
            db, ApplicationStatus.Submitted, clockEightDaysAgo, overrideThresholdDays: 5);
        var mediator = Substitute.For<IMediator>();

        var job = CreateJob(db, mediator, NowClock);
        await job.RunAsync(CancellationToken.None);

        await mediator.Received(1).Send(
            Arg.Is<MarkGhostedCommand>(c => c.ApplicationId == app.Id.Value),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_RespectsPerApplicationThresholdLongerThanDefault()
    {
        var db = TestAppDbContextFactory.Create();
        // 25 dagar bakåt — ÖVER 21-dagars default men INOM en 30-dagars-override
        var clockTwentyFiveDaysAgo = ClockAt(2026, 4, 13);
        await SeedApplicationAsync(
            db, ApplicationStatus.Submitted, clockTwentyFiveDaysAgo, overrideThresholdDays: 30);
        var mediator = Substitute.For<IMediator>();

        var job = CreateJob(db, mediator, NowClock);
        await job.RunAsync(CancellationToken.None);

        await mediator.DidNotReceiveWithAnyArgs().Send(Arg.Any<MarkGhostedCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_MultipleStaleApplications_SendsCommandPerApp()
    {
        var db = TestAppDbContextFactory.Create();
        var pastClock = ClockAt(2026, 4, 1);
        var app1 = await SeedApplicationAsync(db, ApplicationStatus.Submitted, pastClock);
        var app2 = await SeedApplicationAsync(db, ApplicationStatus.Acknowledged, pastClock);
        var app3 = await SeedApplicationAsync(db, ApplicationStatus.Submitted, pastClock);
        var mediator = Substitute.For<IMediator>();

        var job = CreateJob(db, mediator, NowClock);
        await job.RunAsync(CancellationToken.None);

        await mediator.Received(1).Send(
            Arg.Is<MarkGhostedCommand>(c => c.ApplicationId == app1.Id.Value),
            Arg.Any<CancellationToken>());
        await mediator.Received(1).Send(
            Arg.Is<MarkGhostedCommand>(c => c.ApplicationId == app2.Id.Value),
            Arg.Any<CancellationToken>());
        await mediator.Received(1).Send(
            Arg.Is<MarkGhostedCommand>(c => c.ApplicationId == app3.Id.Value),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_RespectsCancellationToken()
    {
        var db = TestAppDbContextFactory.Create();
        var pastClock = ClockAt(2026, 4, 1);
        await SeedApplicationAsync(db, ApplicationStatus.Submitted, pastClock);
        var mediator = Substitute.For<IMediator>();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var job = CreateJob(db, mediator, NowClock);

        await Should.ThrowAsync<OperationCanceledException>(() => job.RunAsync(cts.Token));
    }
}
