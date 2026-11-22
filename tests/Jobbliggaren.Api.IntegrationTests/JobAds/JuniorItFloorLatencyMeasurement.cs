using System.Diagnostics;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
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
using Npgsql;
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
/// by the enabled field's ads rather than by the corpus (ADR 0064, the amendment for #2293, b2).
/// </para>
/// <para>
/// <b>The observation</b> is observe-only (ADR 0045): the handler's three statements on the default view and on the most
/// expensive path a caller can choose, the floor's statement alone, the database time one address can spend at the list's
/// bucket, the corpus's listed ads in the field, and the three plans, all written to the test output for ADR 0190's
/// Measurements. The figures are a developer machine's.
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
        var (catalog, _) = await SeedAsync(db, ct);

        CapturedCommand floor;
        using (var capture = new CommandCapture(db))
        {
            await new JuniorItJobAdQuery(db, catalog).MeetsFloorAsync(DateTimeOffset.UtcNow, ct);
            floor = capture.Commands.ShouldHaveSingleItem();
        }

        var scans = await JobAdsScansAsync(db, floor, ct);
        scans.ShouldNotBeEmpty("The captured statement reads no job_ads: it is not the floor's.");
        scans.Where(scan => scan != IndexName).ShouldBeEmpty(
            $"A scan of job_ads in the floor's statement does not go through {IndexName}: its cost is no longer bounded by "
            + "the field." + Environment.NewLine + string.Join(Environment.NewLine, scans));
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

        IReadOnlyList<CapturedCommand> sent;
        using (var capture = new CommandCapture(db))
        {
            await handler.Handle(costliest, ct);
            sent = capture.Commands;
        }

        sent.Count.ShouldBe(3, "the count, the page and the floor");
        string[] labels = ["the count", $"the page, page {costliest.Page} in the deadline order", "the floor"];
        foreach (var (label, statement) in labels.Zip(sent))
        {
            _output.WriteLine($"EXPLAIN (ANALYZE, BUFFERS), {label}, as sent:");
            foreach (var line in await LatencyMeasurementTools.ExplainAsync(db, statement, ct))
                _output.WriteLine(line);
        }
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

    // Every node of the plan that reads job_ads, named by the index it reads through, or by its node type when it reads none.
    private static async Task<List<string>> JobAdsScansAsync(AppDbContext db, CapturedCommand statement, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var explain = new NpgsqlCommand(
                "EXPLAIN (FORMAT JSON) " + statement.Sql, (NpgsqlConnection)db.Database.GetDbConnection());
            explain.Parameters.AddRange(statement.Parameters.Select(p => p.Clone()).ToArray());
            using var plan = JsonDocument.Parse((string)(await explain.ExecuteScalarAsync(ct))!);
            var scans = new List<string>();
            Walk(plan.RootElement[0].GetProperty("Plan"), scans);
            return scans;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static void Walk(JsonElement node, List<string> scans)
    {
        if (node.TryGetProperty("Relation Name", out var relation) && relation.GetString() == "job_ads")
            scans.Add(AccessPath(node));
        if (node.TryGetProperty("Plans", out var children))
            foreach (var child in children.EnumerateArray())
                Walk(child, scans);
    }

    private static string AccessPath(JsonElement node) => node.GetProperty("Node Type").GetString() switch
    {
        "Index Scan" or "Index Only Scan" => node.GetProperty("Index Name").GetString()!,
        "Bitmap Heap Scan" => string.Join(" + ", BitmapIndexes(node).Distinct()),
        var other => other!,
    };

    private static IEnumerable<string> BitmapIndexes(JsonElement node)
    {
        if (node.GetProperty("Node Type").GetString() == "Bitmap Index Scan")
            yield return node.GetProperty("Index Name").GetString()!;
        if (node.TryGetProperty("Plans", out var children))
            foreach (var child in children.EnumerateArray())
                foreach (var index in BitmapIndexes(child))
                    yield return index;
    }
}
