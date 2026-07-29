using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Applications.Queries.GetActivityReport;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
// Application-typen krockar med Jobbliggaren.Application-namespacet; alias per fil
// (integrationsprojektet saknar global alias, jfr GetActivityReportLocationIntegrationTests).
using DomainApplication = Jobbliggaren.Domain.Applications.Application;

namespace Jobbliggaren.Api.IntegrationTests.Applications;

/// <summary>
/// The Swedish month window as it reaches PostgreSQL (Klas-direktiv 2026-07-28,
/// ADR 0064 Amendment).
///
/// <para>
/// <b>Why this cannot live in the handler's unit tests.</b> The window's two ends
/// are not numbers the handler compares in memory — they are <c>timestamptz</c>
/// QUERY PARAMETERS, and Npgsql writes a <see cref="DateTimeOffset"/> to
/// <c>timestamp with time zone</c> only when the offset is zero. The unit suite
/// runs EF InMemory, which evaluates the same predicate in LINQ-to-Objects where
/// the offset is irrelevant, so it is blind to that rule by construction. The
/// predecessor PR shipped exactly this defect into final review with a fully
/// green unit suite; three reviewers found it by reading, not by running.
/// </para>
/// <para>
/// <b>And an exception-free run is not the assertion.</b> That would prove only
/// that the parameters translate. The EXACT two-row set is what proves the
/// boundary is the Swedish one: each of the four seeded rows dies under a
/// different mutation, named per row below. The DI-resolved adapter's offset
/// contract is pinned separately, in
/// <c>RefreshLandingStatsJobIntegrationTests</c>, and is deliberately not
/// duplicated here.
/// </para>
/// <para>
/// Handler-level rather than through the endpoint, so the window can be named
/// explicitly instead of depending on when CI happens to run. A fresh
/// <c>Guid.NewGuid()</c> owner per run keeps the exact-set assertion immune to
/// the shared integration database.
/// </para>
/// </summary>
[Collection("Api")]
public class GetActivityReportSwedishMonthBoundaryIntegrationTests
{
    private readonly ApiFactory _factory;
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    public GetActivityReportSwedishMonthBoundaryIntegrationTests(ApiFactory factory)
    {
        _factory = factory;
        _currentUser.UserId.Returns(_userId);
    }

    // The Swedish July 2026 is [2026-06-30T22:00Z, 2026-07-31T22:00Z) — both ends
    // at CEST, +02:00.
    private static readonly DateTimeOffset OnTheJulyBoundary =
        new(2026, 6, 30, 22, 0, 0, TimeSpan.Zero);      // 2026-07-01 00:00:00 Swedish — IN
    private static readonly DateTimeOffset OneSecondBefore =
        new(2026, 6, 30, 21, 59, 59, TimeSpan.Zero);    // 2026-06-30 23:59:59 Swedish — OUT
    private static readonly DateTimeOffset TheLastDayOfJuly =
        new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);      // 2026-07-31 14:00 Swedish — IN
    private static readonly DateTimeOffset OnTheAugustBoundary =
        new(2026, 7, 31, 22, 0, 0, TimeSpan.Zero);      // 2026-08-01 00:00:00 Swedish — OUT

    private sealed class FixedClock(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => now;
    }

    private async Task<JobSeeker> SeedSeekerAsync(AppDbContext db, IDateTimeProvider clock)
    {
        var seeker = JobSeeker.Register(_userId, "Test User", clock).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(CancellationToken.None);
        return seeker;
    }

    // The real submit path: Create + TransitionTo(Submitted, clock) is what stamps
    // AppliedAt, so every instant below is one production does produce — a person
    // pressing "skicka" at 00:00:00 Swedish on 1 July produces OnTheJulyBoundary
    // exactly (CLAUDE.md §5 `Tests:`).
    private static DomainApplication SubmittedAt(JobSeekerId seekerId, DateTimeOffset appliedAt)
    {
        var clock = new FixedClock(appliedAt);
        var app = DomainApplication.Create(seekerId, null, null, null, clock).Value;
        app.TransitionTo(ApplicationStatus.Submitted, clock);
        return app;
    }

    [Fact]
    public async Task Handle_OverRealPostgres_TranslatesTheSwedishBoundaryParameters_AndReturnsTheBoundaryRow()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var taxonomy = scope.ServiceProvider.GetRequiredService<ITaxonomyReadModel>();
        var calendar = scope.ServiceProvider.GetRequiredService<ISwedishCalendar>();
        var ct = TestContext.Current.CancellationToken;

        var seeker = await SeedSeekerAsync(db, new FixedClock(OnTheJulyBoundary));
        foreach (var instant in new[]
                 { OnTheJulyBoundary, OneSecondBefore, TheLastDayOfJuly, OnTheAugustBoundary })
        {
            db.Applications.Add(SubmittedAt(seeker.Id, instant));
        }
        await db.SaveChangesAsync(ct);

        var handler = new GetActivityReportQueryHandler(
            db, _currentUser, taxonomy, new FixedClock(TheLastDayOfJuly), calendar);

        var result = await handler.Handle(new GetActivityReportQuery(2026, 7), ct);

        // Reaching this line at all is the offset half: a non-zero-offset
        // parameter throws "Cannot write DateTimeOffset with Offset=02:00:00 to
        // PostgreSQL type 'timestamp with time zone'" before any assertion runs.
        var applied = result.Applications.Select(a => a.AppliedAt).ToList();

        // OnTheJulyBoundary dies under `>=` → `>`, and under a UTC-derived start.
        // TheLastDayOfJuly dies under an end derived as Start.AddMonths(1), which
        // closes the Swedish July a full day early.
        applied.ShouldBe([OnTheJulyBoundary, TheLastDayOfJuly]);

        // OneSecondBefore dies under a start moved a day early; OnTheAugustBoundary
        // under `<` → `<=` and under a UTC-derived end.
        applied.ShouldNotContain(OneSecondBefore);
        applied.ShouldNotContain(OnTheAugustBoundary);
    }
}
