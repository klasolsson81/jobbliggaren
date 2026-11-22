using System.Diagnostics;
using Jobbliggaren.Api.IntegrationTests.Sessions;
using Jobbliggaren.Application.JobAds.Junior;
using Jobbliggaren.Application.JobAds.Publication;
using Jobbliggaren.Application.JobAds.Queries.GetJuniorItJobAdCount;
using Jobbliggaren.Infrastructure.JobAds;
using Jobbliggaren.Infrastructure.JobAds.Junior;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Infrastructure.Taxonomy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// ADR 0188 (#2265) — the latency of the count behind the junior quick link in the /jobb hero, against ADR 0045 class
/// (a), read/list: <b>p95 300 ms</b>, and against the per-account database-time anchor the count's rate-limit bucket is
/// derived from (<c>RateLimitingOptions.JobAdJuniorItCount</c>). It times <see cref="GetJuniorItJobAdCountQueryHandler"/>
/// over the real <see cref="JuniorItJobAdQuery"/> and <see cref="JuniorFieldResolver"/>: the GROUP BY on the kommun under
/// <see cref="JobAdPublication.IsListed"/> and the EXISTS on the stored assessment (<see cref="JuniorMembership"/>).
/// <para>
/// <b>Why this corpus.</b> As <see cref="JuniorItFilterLatencyMeasurement"/>'s, plus what this statement reads and that
/// one does not: every ad carries a kommun from the real taxonomy snapshot (with its län), one in twenty has none, and one
/// in ten has a deadline that has passed, so the count's GROUP BY has the real number of groups and
/// <see cref="JobAdPublication.IsListed"/> excludes rows on all three of its conditions (<see cref="JuniorLatencyCorpus"/>).
/// </para>
/// <para>
/// ⚠ <b>OBSERVE-ONLY</b> (ADR 0045): durations are written to the test output and read into ADR 0188. What is asserted is
/// that the count found ads and that it is a strict subset of the listed ads.
/// </para>
/// </summary>
[Collection("JobAdBrowsePlan")]
[Trait("Category", "SmokeTest")]
public class JuniorItCountLatencyMeasurement(JobAdBrowsePlanFixture fixture, ITestOutputHelper output)
{
    private readonly JobAdBrowsePlanFixture _fixture = fixture;
    private readonly ITestOutputHelper _output = output;

    [Fact]
    public async Task JuniorItCount_LatencyAgainstAdr0045ClassA()
    {
        var ct = TestContext.Current.CancellationToken;
        var scopeFactory = _fixture.Services.GetRequiredService<IServiceScopeFactory>();
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Test");
        await new TaxonomySnapshotSeeder(scopeFactory, environment, NullLogger<TaxonomySnapshotSeeder>.Instance)
            .StartAsync(ct);

        await using var scope = _fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var taxonomy = new TaxonomyReadModel(scopeFactory);
        var catalog = new JuniorFieldResolver([JuniorRuleSetLoader.LoadDataIt()], taxonomy);
        var fields = await catalog.ResolveEnabledAsync(ct);
        var itGroups = JuniorMembership.GroupsOf(fields).Order(StringComparer.Ordinal).ToList();
        var tree = await taxonomy.GetTreeAsync(ct);
        var otherGroups = tree.OccupationFields
            .SelectMany(f => f.OccupationGroups).Select(g => g.ConceptId)
            .Where(g => !itGroups.Contains(g)).ToList();
        var places = tree.Regions
            .SelectMany(r => r.Municipalities.Select(m => (Region: r.ConceptId, Municipality: m.ConceptId)))
            .ToList();
        places.ShouldNotBeEmpty("the taxonomy snapshot carries the kommuner");

        var seeding = Stopwatch.StartNew();
        var verdicts = await JuniorLatencyCorpus.SeedAsync(
            db, new JuniorFieldClassifier(fields), itGroups, otherGroups, places, withOrganisationNumbers: false, ct);
        _output.WriteLine($"Seeded {JuniorLatencyCorpus.TotalRows:N0} ads in {seeding.Elapsed.TotalSeconds:F0} s; "
            + $"{itGroups.Count} Data/IT groups; {places.Count} kommuner; assessments: "
            + $"{string.Join(", ", verdicts.Select(v => $"{v.Value} {v.Key}"))}.");

        var clock = new MutableFakeDateTimeProvider { UtcNow = DateTimeOffset.UtcNow };
        var handler = new GetJuniorItJobAdCountQueryHandler(new JuniorItJobAdQuery(db, catalog), clock);
        var count = (await handler.Handle(new GetJuniorItJobAdCountQuery(), ct)).Count;
        var listed = await db.JobAds.AsNoTracking().CountAsync(JobAdPublication.IsListed(clock.UtcNow), ct);
        count.ShouldBeGreaterThan(0, "a count of nothing times nothing");
        count.ShouldBeLessThan(listed, "the junior count is a strict subset of the listed ads");

        var (p50, p95) = await JuniorLatencyCorpus.MeasureAsync(
            () => handler.Handle(new GetJuniorItJobAdCountQuery(), ct).AsTask(), ct);
        _output.WriteLine($"junior-it-count  {count,6} of {listed,6} listed  p50 {p50,6:F1} ms  p95 {p95,6:F1} ms");

        var groups = JuniorMembership.GroupsOf(fields);
        _output.WriteLine(await JuniorLatencyCorpus.ExplainAsync(db, db.JobAds
            .AsNoTracking()
            .Where(JobAdPublication.IsListed(clock.UtcNow))
            .Where(JuniorMembership.Of(db.JuniorAssessments, groups))
            .GroupBy(j => j.MunicipalityConceptId)
            .Select(g => new { g.Key, Count = g.Count() }), "junior-it-count", ct));
    }
}
