using Jobbliggaren.Application.Applications.Queries.GetApplicationStats;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Applications.Queries.GetApplicationStats;

// #313 — handler WIRING coverage on the EF InMemory provider: JobSeeker scoping,
// anonymous / no-seeker → empty stats, soft-delete exclusion (carried solely by
// the global query filter, ADR 0048 c), and the status+AppliedAt projection
// feeding the calculator. The metric MATH is covered exhaustively (and DB-free) in
// ApplicationStatsCalculatorTests; these prove the handler hands the calculator the
// right rows. The .Take-valve + real SQL translation are pinned in the Testcontainers
// integration smoke.
public class GetApplicationStatsQueryHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    // 2026-06-15 — apply dates stamped via TransitionTo land in the current month.
    private readonly FakeDateTimeProvider _clock =
        new(new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero));

    public GetApplicationStatsQueryHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private GetApplicationStatsQueryHandler CreateHandler(AppDbContext db, ICurrentUser? user = null) =>
        new(db, user ?? _currentUser, _clock);

    private async Task<JobSeeker> SeedSeekerAsync(AppDbContext db, Guid userId, CancellationToken ct)
    {
        var seeker = JobSeeker.Register(userId, "Test", _clock).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);
        return seeker;
    }

    private DomainApplication Draft(JobSeekerId seekerId) =>
        DomainApplication.Create(seekerId, null, null, ManualVo(), _clock).Value;

    private DomainApplication SentWithStatus(JobSeekerId seekerId, params ApplicationStatus[] path)
    {
        var app = DomainApplication.Create(seekerId, null, null, ManualVo(), _clock).Value;
        foreach (var step in path)
            app.TransitionTo(step, _clock);
        return app;
    }

    private static ManualPosting ManualVo() =>
        ManualPosting.Create("Titel", "Företag", "https://example.com/jobb", null).Value;

    // ---------------------------------------------------------------
    // Anonymous / no-seeker → empty stats (single calculator code path)
    // ---------------------------------------------------------------

    [Fact]
    public async Task Handle_NoAuthenticatedUser_ReturnsEmptyStats()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var anon = Substitute.For<ICurrentUser>();
        anon.UserId.Returns((Guid?)null);

        var result = await CreateHandler(db, anon).Handle(new GetApplicationStatsQuery(), ct);

        result.TotalApplications.ShouldBe(0);
        result.TotalSent.ShouldBe(0);
        result.StatusCounts.Count.ShouldBe(10);
        result.MonthlyApplications.Count.ShouldBe(12);
    }

    [Fact]
    public async Task Handle_AuthenticatedButNoJobSeeker_ReturnsEmptyStats()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        // No seeker registered for _userId.

        var result = await CreateHandler(db).Handle(new GetApplicationStatsQuery(), ct);

        result.TotalApplications.ShouldBe(0);
        result.TotalSent.ShouldBe(0);
    }

    // ---------------------------------------------------------------
    // JobSeeker scoping — only the current user's applications
    // ---------------------------------------------------------------

    [Fact]
    public async Task Handle_ScopesToCurrentJobSeeker()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var mine = await SeedSeekerAsync(db, _userId, ct);
        var other = await SeedSeekerAsync(db, Guid.NewGuid(), ct);

        db.Applications.Add(SentWithStatus(mine.Id, ApplicationStatus.Submitted));
        // Five applications belonging to another user must NOT leak in.
        for (var i = 0; i < 5; i++)
            db.Applications.Add(SentWithStatus(other.Id, ApplicationStatus.Submitted));
        await db.SaveChangesAsync(ct);

        var result = await CreateHandler(db).Handle(new GetApplicationStatsQuery(), ct);

        result.TotalApplications.ShouldBe(1);
        result.TotalSent.ShouldBe(1);
    }

    // ---------------------------------------------------------------
    // Status + AppliedAt projection → calculator metrics
    // ---------------------------------------------------------------

    [Fact]
    public async Task Handle_ProjectsStatusesAndAppliedAt_FeedingCalculatorMetrics()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId, ct);

        db.Applications.Add(Draft(seeker.Id));                                   // not sent
        db.Applications.Add(SentWithStatus(seeker.Id, ApplicationStatus.Submitted));
        db.Applications.Add(SentWithStatus(
            seeker.Id, ApplicationStatus.Submitted, ApplicationStatus.Acknowledged));
        db.Applications.Add(SentWithStatus(
            seeker.Id, ApplicationStatus.Submitted, ApplicationStatus.Rejected));
        await db.SaveChangesAsync(ct);

        var result = await CreateHandler(db).Handle(new GetApplicationStatsQuery(), ct);

        result.TotalApplications.ShouldBe(4);
        result.TotalSent.ShouldBe(3); // draft excluded
        result.RejectionRate.Numerator.ShouldBe(1);
        result.RejectionRate.Denominator.ShouldBe(3);
        result.ResponseRate.Numerator.ShouldBe(1); // only the Acknowledged one responded
        // All three sent applications applied in the current month (clock).
        result.MonthlyApplications.Single(m => m is { Year: 2026, Month: 6 }).Count.ShouldBe(3);
    }

    // ---------------------------------------------------------------
    // Soft-delete carried solely by the global query filter
    // ---------------------------------------------------------------

    [Fact]
    public async Task Handle_ExcludesSoftDeletedApplications()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId, ct);

        var live = SentWithStatus(seeker.Id, ApplicationStatus.Submitted);
        var deleted = SentWithStatus(seeker.Id, ApplicationStatus.Submitted);
        deleted.SoftDelete(_clock);
        db.Applications.Add(live);
        db.Applications.Add(deleted);
        await db.SaveChangesAsync(ct);

        var result = await CreateHandler(db).Handle(new GetApplicationStatsQuery(), ct);

        // The soft-deleted application is excluded by the global filter — no manual
        // DeletedAt predicate in the handler (ADR 0048 c).
        result.TotalApplications.ShouldBe(1);
        result.TotalSent.ShouldBe(1);
    }
}
