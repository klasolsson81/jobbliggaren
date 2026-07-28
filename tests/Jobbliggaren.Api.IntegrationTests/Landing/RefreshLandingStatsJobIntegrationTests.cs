using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Landing.Common;
using Jobbliggaren.Application.Landing.Jobs.RefreshLandingStats;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Landing;

/// <summary>
/// The gate the unit tests structurally cannot be.
///
/// <para>
/// <b>Why this file exists.</b> The Swedish day boundary is not just a number —
/// it is a <c>timestamptz</c> query PARAMETER. Npgsql writes a
/// <see cref="DateTimeOffset"/> to <c>timestamp with time zone</c> only when
/// <c>Offset == 0</c>; a non-zero offset throws. The unit tests run against EF
/// InMemory, which evaluates the same predicate in LINQ-to-Objects where offset
/// is irrelevant, so they are blind to it by construction — 17k green tests
/// could not answer the question.
/// </para>
/// <para>
/// This was not hypothetical. The first version of <c>SwedishCalendar</c>
/// returned <c>+01:00</c>/<c>+02:00</c>, and <b>three independent reviewers</b>
/// graded it a Blocker citing this repository's own rule: `PlatsbankenJobSource`
/// normalises JobTech dates at the ACL boundary for exactly this reason, and
/// records that the same bug was invisible on a UTC host and fired locally in
/// Sweden at +02:00. My own mutation testing had been arithmetically correct and
/// still walked straight past it, because every assertion compared instants and
/// none compared offsets.
/// </para>
/// <para>
/// So the assertion here is deliberately weak on the COUNT and strong on the
/// TRANSLATION. The integration database is shared, so an exact total cannot be
/// claimed (see the seed-contamination note in the Api suite); what only real
/// Postgres can prove is that the predicate translates and executes at all.
/// </para>
/// </summary>
[Collection("Api")]
public class RefreshLandingStatsJobIntegrationTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    [Fact]
    public async Task RunAsync_OverRealPostgres_TranslatesTheSwedishBoundaryParameter()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var calendar = scope.ServiceProvider.GetRequiredService<ISwedishCalendar>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        // Published just now, so the row is inside the current Swedish day
        // whenever the suite runs. An earlier version seeded a fixed May 2026
        // date while resolving the REAL clock — which made the seed decorative
        // (a two-month-old ad can never be "new today") and its comment true of
        // a clock this test does not use. test-writer caught it.
        //
        // Seeded through the real factory; an ad published moments ago is a
        // value production emits, so this carries no premise obligation under
        // CLAUDE.md §5 `Tests:`.
        // Known, accepted: the seed reads the clock and the job reads it again,
        // so a run landing in the sub-second window at Swedish midnight could
        // put the seed on the far side of the new boundary. ~200 ms per day, and
        // the shared database almost certainly holds other fresh rows — but the
        // guarantee is not absolute. Recorded rather than engineered around.
        var now = clock.UtcNow;
        var ad = JobAd.Create(
            $"swedish-boundary-probe-{Guid.NewGuid():N}",
            Company.Create("Acme").Value,
            "Description",
            $"https://example.com/{Guid.NewGuid():N}",
            JobSource.Manual,
            now,
            now.AddDays(30),
            clock).Value;
        db.JobAds.Add(ad);
        await db.SaveChangesAsync(ct);

        var cache = Substitute.For<ILandingStatsCache>();
        LandingStatsDto? captured = null;
        await cache.SetAsync(Arg.Do<LandingStatsDto>(s => captured = s), Arg.Any<CancellationToken>());

        var job = new RefreshLandingStatsJob(
            db, clock, calendar, cache, NullLogger<RefreshLandingStatsJob>.Instance);

        // Throws on a non-zero-offset parameter. That throw IS the regression
        // this test exists to catch; a green run is the proof the unit suite
        // cannot supply.
        await job.RunAsync(ct);

        captured.ShouldNotBeNull();
        captured!.ActiveCount.ShouldNotBeNull();
        captured.IsStale.ShouldBeFalse();

        // Weak on the count because the integration database is shared; strong
        // on the fact that a row seeded moments ago fell inside the boundary. A
        // boundary computed a day too far forward returns 0 here, so this is a
        // real kill and not just "the query ran".
        captured.NewToday.ShouldNotBeNull();
        captured.NewToday!.Value.ShouldBeGreaterThanOrEqualTo(1,
            "raden publicerades nyss och måste ligga innanför den svenska dygnsgränsen");
    }

    [Fact]
    public async Task SwedishCalendar_ResolvedFromTheRealContainer_ReturnsAZeroOffsetInstant()
    {
        // The DI-resolved instance, not a hand-constructed one: a future
        // registration swap to a decorator or a differently-configured adapter
        // has to keep the property the query depends on.
        using var scope = _factory.Services.CreateScope();
        var calendar = scope.ServiceProvider.GetRequiredService<ISwedishCalendar>();

        calendar.StartOfDay(new DateTimeOffset(2026, 7, 15, 14, 0, 0, TimeSpan.Zero))
            .Offset.ShouldBe(TimeSpan.Zero);
        calendar.StartOfMonth(2026, 1).Offset.ShouldBe(TimeSpan.Zero);
    }
}
