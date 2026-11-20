using System.Diagnostics;
using System.Text;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Internal;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.JobAds;
using Jobbliggaren.Infrastructure.JobAds.JuniorIt;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Infrastructure.Taxonomy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// #2261 — the latency of the junior IT filter on /jobb's search port against ADR 0045 class (a), read/list:
/// <b>p95 300 ms</b>. The filter puts the list's case-insensitive regex (<c>~*</c>) on the hot path, in the list, its
/// count and every facet clone (ADR 0186 "What comes back"; AGENTS.md §2.5).
/// <para>
/// <b>Why this corpus.</b> About a third of the ads sit in the Data/IT field's groups, from the real taxonomy
/// snapshot, and a share of their titles match the shipped headline pattern, so the regex runs on every
/// in-field row the planner reaches. A few rows carry ids from the shipped curated list. The cases dotnet-architect
/// named are measured: junior alone, junior with one Data/IT group in Yrke (two conditions on one column), and
/// junior with free text.
/// </para>
/// <para>
/// ⚠ <b>OBSERVE-ONLY</b> (ADR 0045, as <see cref="OccupationNameLatencyMeasurement"/>): durations are written to the
/// test output and read into the PR body. What is asserted is that each search found ads and that the junior
/// list is a strict subset of the unfiltered one.
/// </para>
/// <para>
/// <b>Result, measured 2026-11-20 against this class at 40 000 ads (32 000 active), 12 Data/IT groups, 143 curated
/// ids.</b> A dated reading of a finished run on a developer machine, not a live number: re-run this class to
/// regenerate it.
/// <code>
/// search                                     ads/keys   p50       p95
/// list+count (no filter)                     32 000     6.8 ms     8.9 ms
/// list+count junior                           2 680    34.8 ms    51.4 ms
/// list+count junior + 1 Data/IT group           227     9.1 ms    16.1 ms
/// list+count junior, q = "utvecklare"           915    20.1 ms    28.0 ms
/// group facet, junior                            12    29.8 ms    40.8 ms
/// group facet, junior, q                         12    17.5 ms    18.7 ms
/// </code>
/// </para>
/// </summary>
[Collection("JobAdBrowsePlan")]
[Trait("Category", "SmokeTest")]
public class JuniorItFilterLatencyMeasurement(JobAdBrowsePlanFixture fixture, ITestOutputHelper output)
{
    private const int TotalRows = 40_000;
    private const int CuratedRows = 100;
    private const int Iterations = 30;

    private readonly JobAdBrowsePlanFixture _fixture = fixture;
    private readonly ITestOutputHelper _output = output;

    [Fact]
    public async Task JuniorIt_LatencyAgainstAdr0045ClassA()
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
        var list = JuniorItListLoader.Load();
        var juniorIt = new JuniorItSetResolver(list, taxonomy);
        var set = await juniorIt.ResolveAsync(ct);
        var tree = await taxonomy.GetTreeAsync(ct);
        var otherGroups = tree.OccupationFields
            .Where(f => !string.Equals(f.ConceptId, list.OccupationFieldConceptId, StringComparison.Ordinal))
            .SelectMany(f => f.OccupationGroups).Select(g => g.ConceptId).ToList();

        // Built by hand as the sibling measurements build it: the fixture composes persistence only, and no search
        // here carries a minus-word, so the label matcher is never asked.
        var resolver = new JobAdSearchTextResolver(
            new SearchQueryParser(),
            new OccupationSynonymExpander(Options.Create(new SearchSynonymsOptions())),
            Substitute.For<ITaxonomyLabelMatcher>(),
            scope.ServiceProvider.GetRequiredService<IOccupationNameLineageProvider>(),
            juniorIt);
        var search = new JobAdSearchQuery(db, resolver);

        var seeding = Stopwatch.StartNew();
        await SeedAsync(db, set, otherGroups, ct);
        _output.WriteLine($"Seeded {TotalRows:N0} ads in {seeding.Elapsed.TotalSeconds:F0} s; "
            + $"{set.OccupationGroups.Count} Data/IT groups, {set.CuratedExternalIds.Count} curated ids.");

        var itGroup = set.OccupationGroups[0];
        var all = (await search.SearchAsync(Page(Filter()), ct)).TotalCount;
        var scenarios = new (string Label, JobAdFilterCriteria Filter)[]
        {
            ("(no filter)", Filter()),
            ("junior", Filter(junior: true)),
            ("junior + 1 Data/IT group", Filter(groups: [itGroup], junior: true)),
            ("junior, q = \"utvecklare\"", Filter(q: "utvecklare", junior: true)),
        };

        foreach (var (label, filter) in scenarios)
        {
            var total = (await search.SearchAsync(Page(filter), ct)).TotalCount;
            total.ShouldBeGreaterThan(0, $"{label}: a search that finds nothing times nothing");
            if (filter.JuniorIt)
                total.ShouldBeLessThan(all, $"{label}: the junior list is a strict subset of the corpus");
            var (p50, p95) = await MeasureAsync(() => search.SearchAsync(Page(filter), ct).AsTask(), ct);
            _output.WriteLine($"list+count  {label,-30} {total,6} ads  p50 {p50,6:F1} ms  p95 {p95,6:F1} ms");
        }

        foreach (var (label, filter) in new[]
                 {
                     ("group facet, junior", Filter(junior: true)),
                     ("group facet, junior, q", Filter(q: "utvecklare", junior: true)),
                 })
        {
            var counts = await search.FacetCountsAsync(filter, FacetDimension.OccupationGroup, ct);
            counts.ShouldNotBeEmpty(label);
            var (p50, p95) = await MeasureAsync(
                () => search.FacetCountsAsync(filter, FacetDimension.OccupationGroup, ct).AsTask(), ct);
            _output.WriteLine($"facet       {label,-30} {counts.Count,6} keys p50 {p50,6:F1} ms  p95 {p95,6:F1} ms");
        }

        foreach (var (label, filter) in new[]
                 {
                     ("junior", Filter(junior: true)),
                     ("junior + 1 Data/IT group", Filter(groups: [itGroup], junior: true)),
                 })
        {
            var resolved = await resolver.ResolveAsync(filter, ct);
            _output.WriteLine(await ExplainAsync(
                db, label, JobAdSearchComposition.ApplyFilter(db.JobAds.AsNoTracking(), resolved), ct));
        }
    }

    private static JobAdFilterCriteria Filter(
        IReadOnlyList<string>? groups = null, string? q = null, bool junior = false) => new(
        OccupationGroup: groups ?? [],
        OccupationName: [],
        Municipality: [],
        Region: [],
        EmploymentType: [],
        WorktimeExtent: [],
        Employer: [],
        Remote: false,
        JuniorIt: junior,
        Q: q);

    private static JobAdSearchCriteria Page(JobAdFilterCriteria filter) =>
        new(filter, JobAdSortBy.PublishedAtDesc, Page: 1, PageSize: 20);

    private static async Task<(double P50, double P95)> MeasureAsync(Func<Task> call, CancellationToken ct)
    {
        for (var i = 0; i < 3; i++)
            await call();

        var samples = new List<double>(Iterations);
        for (var i = 0; i < Iterations; i++)
        {
            ct.ThrowIfCancellationRequested();
            var sw = Stopwatch.StartNew();
            await call();
            sw.Stop();
            samples.Add(sw.Elapsed.TotalMilliseconds);
        }

        samples.Sort();
        return (Percentile(samples, 0.50), Percentile(samples, 0.95));
    }

    private static double Percentile(List<double> sorted, double p) =>
        sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Count) - 1, 0, sorted.Count - 1)];

    private static async Task<string> ExplainAsync(
        AppDbContext db, string label, IQueryable<JobAd> filtered, CancellationToken ct)
    {
        await using var command = filtered.Select(j => j.Id).CreateDbCommand();
        command.CommandText = "EXPLAIN " + command.CommandText;
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var plan = new StringBuilder($"EXPLAIN, {label}:\n");
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                plan.AppendLine(reader.GetString(0));
            return plan.ToString();
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// TRUNCATE-and-own, bulk-seed, then VACUUM (ANALYZE). A third of the ads draw a Data/IT group; of those, a fifth
    /// carry a junior headline, a twentieth a trainee one, and the first <see cref="CuratedRows"/> a curated id.
    /// Every third title carries "utvecklare" for the free-text case.
    /// </summary>
    private static async Task SeedAsync(
        AppDbContext db, JuniorItSet set, List<string> otherGroups, CancellationToken ct)
    {
        db.Database.SetCommandTimeout(600);
        await db.Database.ExecuteSqlRawAsync("TRUNCATE job_ads;", ct);

        var random = new Random(2261);
        var groups = new string[TotalRows];
        var titles = new string[TotalRows];
        var externalIds = new string[TotalRows];
        var curated = 0;
        for (var i = 0; i < TotalRows; i++)
        {
            var inField = random.NextDouble() < 1.0 / 3;
            groups[i] = inField
                ? set.OccupationGroups[random.Next(set.OccupationGroups.Count)]
                : otherGroups[random.Next(otherGroups.Count)];
            var roll = random.NextDouble();
            var noun = i % 3 == 0 ? "utvecklare" : "medarbetare";
            titles[i] = roll < 0.2 ? $"Junior {noun} {i}" : roll < 0.25 ? $"Trainee {noun} {i}" : $"System{noun} {i}";
            externalIds[i] = inField && curated < CuratedRows && curated < set.CuratedExternalIds.Count
                ? set.CuratedExternalIds[curated++]
                : $"lat-{i}";
        }

        await using (var connection = new NpgsqlConnection(db.Database.GetConnectionString()))
        {
            await connection.OpenAsync(ct);
            await using var insert = new NpgsqlCommand(
                "INSERT INTO job_ads "
                + "(id, title, description, url, published_at, created_at, status, source, company_name, remote, "
                + "occupation_group_concept_id, external_source, external_id) "
                + "SELECT gen_random_uuid(), u.title, "
                + "'Beskrivning av tjänsten nummer ' || u.i, "
                + "'https://example.com/jobb/' || u.i, "
                + "now() - (((u.i * 7919) % " + TotalRows + ") || ' minutes')::interval, now(), "
                + "CASE WHEN u.i % 5 = 0 THEN 'Archived' ELSE 'Active' END, 'Platsbanken', "
                + "'Arbetsgivare ' || (u.i % 997) || ' AB', false, u.grp, 'Platsbanken', u.ext "
                + "FROM unnest(@groups, @titles, @ids) WITH ORDINALITY AS u(grp, title, ext, i);",
                connection)
            {
                CommandTimeout = 600,
            };
            insert.Parameters.AddWithValue("groups", groups);
            insert.Parameters.AddWithValue("titles", titles);
            insert.Parameters.AddWithValue("ids", externalIds);
            await insert.ExecuteNonQueryAsync(ct);
        }

        await db.Database.ExecuteSqlRawAsync("VACUUM (ANALYZE) job_ads;", ct);
    }
}
