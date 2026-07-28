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

        // 00:30 Swedish time on 23 May 2026 — inside the Swedish day, outside the
        // retired UTC one. Seeded through the real factory; a published-at inside
        // that window is a value production emits (ads are published around the
        // clock), so this carries no premise obligation under CLAUDE.md §5.
        var ad = JobAd.Create(
            $"swedish-boundary-probe-{Guid.NewGuid():N}",
            Company.Create("Acme").Value,
            "Description",
            $"https://example.com/{Guid.NewGuid():N}",
            JobSource.Manual,
            new DateTimeOffset(2026, 5, 22, 22, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 21, 22, 30, 0, TimeSpan.Zero),
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
        captured.NewToday.ShouldNotBeNull();
        captured.IsStale.ShouldBeFalse();
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
