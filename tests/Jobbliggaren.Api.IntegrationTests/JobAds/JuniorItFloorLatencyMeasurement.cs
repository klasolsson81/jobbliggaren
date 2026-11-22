using System.Diagnostics;
using Jobbliggaren.Api.IntegrationTests.Sessions;
using Jobbliggaren.Application.JobAds.Junior;
using Jobbliggaren.Application.JobAds.Publication;
using Jobbliggaren.Application.JobAds.Queries.GetPublicJuniorItJobAds;
using Jobbliggaren.Domain.JobAds;
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
/// #2293 (ADR 0166, the Amendment 2026-11-22; ADR 0064, rule (d)) — the junior list's floor for indexing on
/// <see cref="JuniorLatencyCorpus"/>, with an organisation number on its employers. One pin and one observation.
/// </summary>
/// <remarks>
/// <para>
/// <b>The pin:</b> every scan of <c>job_ads</c> in the floor's statement goes through
/// <c>ix_job_ads_occupation_group_concept_id</c>, and none is a sequential scan. That is what bounds the statement's cost
/// by the enabled field's listed ads rather than by the corpus (ADR 0064, the amendment for #2293, b2). The statement is
/// composed by the production members, <see cref="PublicListFloor.SpreadOf"/> over the list's base, and its base is pinned
/// equal to the port's in <c>JuniorItJobAdQueryReadTests</c>.
/// </para>
/// <para>
/// <b>The observation</b> is observe-only (ADR 0045): the handler's three statements on the default view and on the most
/// expensive path a caller can choose, the floor's statement alone, the database time one address can spend at the list's
/// bucket, the corpus's listed ads in the field, and the three plans, all written to the test output for ADR 0190's
/// Measurements. The figures are a developer machine's, so they are a floor on what the box costs.
/// </para>
/// </remarks>
[Collection("JobAdBrowsePlan")]
[Trait("Category", "SmokeTest")]
public class JuniorItFloorLatencyMeasurement(JobAdBrowsePlanFixture fixture, ITestOutputHelper output)
{
    private const string IndexName = "ix_job_ads_occupation_group_concept_id";

    private readonly JobAdBrowsePlanFixture _fixture = fixture;
    private readonly ITestOutputHelper _output = output;

    [Fact]
    public async Task TheFloorStatement_ScansJobAdsOnlyThroughTheOccupationGroupIndex()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = _fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, groups) = await SeedAsync(db, ct);

        var plan = await JuniorLatencyCorpus.ExplainAsync(db, FloorStatement(db, DateTimeOffset.UtcNow, groups), "the floor", ct);

        plan.ShouldContain(IndexName, Case.Insensitive,
            $"The floor's statement no longer reaches {IndexName}: its cost is bounded by the corpus, not by the field."
            + Environment.NewLine + plan);
        plan.ShouldNotContain("Seq Scan on job_ads", Case.Insensitive,
            "A scan of job_ads outside the occupation-group index: the floor's cost is no longer bounded by the field."
            + Environment.NewLine + plan);
    }

    [Fact]
    public async Task JuniorItFloor_LatencyAgainstAdr0045ClassA()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = _fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seeding = Stopwatch.StartNew();
        var (catalog, groups) = await SeedAsync(db, ct);
        _output.WriteLine($"Seeded {JuniorLatencyCorpus.TotalRows:N0} ads in {seeding.Elapsed.TotalSeconds:F0} s.");

        var clock = new MutableFakeDateTimeProvider { UtcNow = DateTimeOffset.UtcNow };
        var port = new JuniorItJobAdQuery(db, catalog);
        var handler = new GetPublicJuniorItJobAdsQueryHandler(port, new TaxonomyReadModel(
            _fixture.Services.GetRequiredService<IServiceScopeFactory>()), clock);
        var defaultView = new GetPublicJuniorItJobAdsQuery(null, JobAdSortBy.PublishedAtDesc, 1);

        var page = (await handler.Handle(defaultView, ct)).ShouldBeOfType<PublicJuniorItJobAdsPage.Available>();
        page.Rows.TotalCount.ShouldBeGreaterThan(0, "a floor over nothing times nothing");
        page.FloorHolds.ShouldBeTrue("the corpus clears the floor, so its statement is evaluated whole");
        var inField = await db.JobAds.AsNoTracking()
            .Where(JobAdPublication.IsListed(clock.UtcNow))
            .Where(JuniorMembership.InFields(groups))
            .CountAsync(ct);

        // The most expensive path a caller can choose: every kommun, the deadline order, the deepest page the list allows.
        var lastPage = Math.Max(1, (page.Rows.TotalCount + PublicJuniorItJobAdLimits.PageSize - 1) / PublicJuniorItJobAdLimits.PageSize);
        var costliest = new GetPublicJuniorItJobAdsQuery(
            null, JobAdSortBy.ExpiresAtAsc, Math.Min(lastPage, PublicJuniorItJobAdLimits.MaxPage));

        var (defaultP50, defaultP95) = await JuniorLatencyCorpus.MeasureAsync(() => handler.Handle(defaultView, ct).AsTask(), ct);
        var (costliestP50, costliestP95) = await JuniorLatencyCorpus.MeasureAsync(() => handler.Handle(costliest, ct).AsTask(), ct);
        var (floorP50, floorP95) = await JuniorLatencyCorpus.MeasureAsync(
            () => port.MeetsFloorAsync(clock.UtcNow, ct).AsTask(), ct);
        var bucket = new Jobbliggaren.Api.RateLimiting.RateLimitingOptions().PublicJobAdJuniorIt;
        var perMinute = bucket.PermitLimit * 60.0 / bucket.WindowSeconds;

        _output.WriteLine($"corpus: {inField} listed ads in the field, {page.Rows.TotalCount} listed juniors");
        _output.WriteLine($"default view, three statements           p50 {defaultP50,6:F1} ms  p95 {defaultP95,6:F1} ms");
        _output.WriteLine($"page {costliest.Page}, deadline order, three statements  p50 {costliestP50,6:F1} ms  p95 {costliestP95,6:F1} ms");
        _output.WriteLine($"the floor alone                           p50 {floorP50,6:F1} ms  p95 {floorP95,6:F1} ms");
        _output.WriteLine($"one address at {perMinute:F0} a minute: {costliestP95 * perMinute:F0} ms of database time a minute at the "
            + $"costliest path's p95, {defaultP95 * perMinute:F0} ms at the default view's, of which the floor is "
            + $"{floorP95 * perMinute:F0} ms.");

        _output.WriteLine(await JuniorLatencyCorpus.ExplainAsync(db, FloorStatement(db, clock.UtcNow, groups), "the floor", ct));
        _output.WriteLine(await JuniorLatencyCorpus.ExplainAsync(db, ListedJuniors(db, clock.UtcNow, groups)
            .GroupBy(j => j.MunicipalityConceptId)
            .Select(g => new { g.Key, Count = g.Count() }), "the count", ct));
        _output.WriteLine(await JuniorLatencyCorpus.ExplainAsync(db, JobAdSearchComposition
            .ApplySort(ListedJuniors(db, clock.UtcNow, groups), JobAdSortBy.ExpiresAtAsc,
                new ResolvedSearchText(null, [], Application.JobAds.Abstractions.TaxonomyLabelMatchDto.Empty))
            .Skip((costliest.Page - 1) * PublicJuniorItJobAdLimits.PageSize)
            .Take(PublicJuniorItJobAdLimits.PageSize)
            .Select(j => new { j.Id, j.Title }), $"the page, page {costliest.Page} in the deadline order", ct));
    }

    private async Task<(JuniorFieldResolver Catalog, IReadOnlyCollection<string> Groups)> SeedAsync(
        AppDbContext db, CancellationToken ct)
    {
        var scopeFactory = _fixture.Services.GetRequiredService<IServiceScopeFactory>();
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Test");
        await new TaxonomySnapshotSeeder(scopeFactory, environment, NullLogger<TaxonomySnapshotSeeder>.Instance)
            .StartAsync(ct);

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

        await JuniorLatencyCorpus.SeedAsync(
            db, new JuniorFieldClassifier(fields), itGroups, otherGroups, places, withOrganisationNumbers: true, ct);
        return (catalog, JuniorMembership.GroupsOf(fields));
    }

    // The port's base, composed the way JuniorItJobAdQueryReadTests pins the port's own statements to.
    private static IQueryable<JobAd> ListedJuniors(AppDbContext db, DateTimeOffset now, IReadOnlyCollection<string> groups) =>
        db.JobAds.AsNoTracking()
            .Where(JobAdPublication.IsListed(now))
            .Where(JuniorMembership.Of(db.JuniorAssessments, groups));

    // The statement the port sends inside EXISTS.
    private static IQueryable<EmployerSpread> FloorStatement(
        AppDbContext db, DateTimeOffset now, IReadOnlyCollection<string> groups) =>
        PublicListFloor.SpreadOf(ListedJuniors(db, now, groups)).Where(PublicListFloor.Holds);
}
