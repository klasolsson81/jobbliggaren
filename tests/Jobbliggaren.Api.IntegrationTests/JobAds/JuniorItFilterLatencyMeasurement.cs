using System.Diagnostics;
using System.Text;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Internal;
using Jobbliggaren.Application.JobAds.Junior;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.JobAds;
using Jobbliggaren.Infrastructure.JobAds.Junior;
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
/// #2261, #2264 — the latency of the junior IT filter on /jobb's search port against ADR 0045 class (a), read/list:
/// <b>p95 300 ms</b>. The filter puts a correlated EXISTS on the stored assessment (<see cref="JuniorMembership"/>,
/// ADR 0187) on the hot path, in the list, its count and every facet clone (AGENTS.md §2.5).
/// <para>
/// <b>Why this corpus.</b> About a third of the ads sit in the Data/IT field's groups, from the real taxonomy
/// snapshot. Each in-field ad carries the assessment the shipped rules give its title and text, computed by the
/// production <see cref="JuniorClassifier"/> and bulk-written in the shape the Worker's sweep stores: the steady
/// state after a sweep, one assessment per in-field ad. The titles mix junior and trainee headlines (an automatic
/// rule, so Junior), student headlines (a rule that is not automatic, so Unclear) and plain ones (no signal). The
/// cases dotnet-architect named are measured: junior alone, junior with one Data/IT group in Yrke (two conditions on
/// one column), and junior with free text.
/// </para>
/// <para>
/// ⚠ <b>OBSERVE-ONLY</b> (ADR 0045, as <see cref="OccupationNameLatencyMeasurement"/>): durations are written to the
/// test output and read into the PR body. What is asserted is that each search found ads and that the junior
/// list is a strict subset of the unfiltered one.
/// </para>
/// <para>
/// <b>Result, measured 2026-11-20 against this class at 40 000 ads (32 000 active), 12 Data/IT groups, and the shipped
/// rules' assessments on the 13 286 in-field ads (3 301 Junior, 709 Unclear, 9 276 NotJunior).</b> A dated reading of a
/// finished run on a developer machine, not a live number: re-run this class to regenerate it.
/// <code>
/// search                                     ads/keys   p50       p95
/// list+count (no filter)                     32 000     4.0 ms     4.2 ms
/// list+count junior                           2 619     6.5 ms     9.6 ms
/// list+count junior + 1 Data/IT group           220     5.5 ms     5.8 ms
/// list+count junior, q = "utvecklare"           896    15.8 ms    19.3 ms
/// group facet, junior                            12     5.5 ms     6.1 ms
/// group facet, junior, q                         12    14.9 ms    17.7 ms
/// </code>
/// </para>
/// </summary>
[Collection("JobAdBrowsePlan")]
[Trait("Category", "SmokeTest")]
public class JuniorItFilterLatencyMeasurement(JobAdBrowsePlanFixture fixture, ITestOutputHelper output)
{
    private const int TotalRows = 40_000;
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
        var catalog = new JuniorFieldResolver([JuniorRuleSetLoader.LoadDataIt()], taxonomy);
        var fields = await catalog.ResolveEnabledAsync(ct);
        var itGroups = JuniorMembership.GroupsOf(fields).Order(StringComparer.Ordinal).ToList();
        var tree = await taxonomy.GetTreeAsync(ct);
        var otherGroups = tree.OccupationFields
            .SelectMany(f => f.OccupationGroups).Select(g => g.ConceptId)
            .Where(g => !itGroups.Contains(g)).ToList();

        // Built by hand as the sibling measurements build it: the fixture composes persistence only, and no search
        // here carries a minus-word, so the label matcher is never asked.
        var resolver = new JobAdSearchTextResolver(
            new SearchQueryParser(),
            new OccupationSynonymExpander(Options.Create(new SearchSynonymsOptions())),
            Substitute.For<ITaxonomyLabelMatcher>(),
            scope.ServiceProvider.GetRequiredService<IOccupationNameLineageProvider>(),
            db,
            catalog);
        var search = new JobAdSearchQuery(db, resolver);

        var seeding = Stopwatch.StartNew();
        var verdicts = await SeedAsync(db, new JuniorFieldClassifier(fields), itGroups, otherGroups, ct);
        _output.WriteLine($"Seeded {TotalRows:N0} ads in {seeding.Elapsed.TotalSeconds:F0} s; {itGroups.Count} Data/IT "
            + $"groups; assessments: {string.Join(", ", verdicts.Select(v => $"{v.Value} {v.Key}"))}.");
        verdicts.Keys.ShouldBe([JuniorVerdict.Junior, JuniorVerdict.NotJunior, JuniorVerdict.Unclear], ignoreOrder: true);

        var itGroup = itGroups[0];
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
    /// TRUNCATE-and-own, bulk-seed, then VACUUM (ANALYZE) both tables. A third of the ads draw a Data/IT group; of
    /// those, a fifth carry a junior headline, a twentieth a trainee one and a twentieth a student one. Every third
    /// title carries "utvecklare" for the free-text case. Each in-field ad gets the classifier's verdict for its own
    /// title and text, stamped with the null fingerprint the sweep reads off a row this insert writes.
    /// </summary>
    private static async Task<Dictionary<JuniorVerdict, int>> SeedAsync(
        AppDbContext db,
        JuniorFieldClassifier classifier,
        List<string> itGroups,
        List<string> otherGroups,
        CancellationToken ct)
    {
        db.Database.SetCommandTimeout(600);
        await db.Database.ExecuteSqlRawAsync("TRUNCATE job_ads CASCADE;", ct);

        var random = new Random(2261);
        var ids = new Guid[TotalRows];
        var groups = new string[TotalRows];
        var titles = new string[TotalRows];
        var descriptions = new string[TotalRows];
        var externalIds = new string[TotalRows];
        var assessed = new List<(Guid Id, JuniorClassification Classification)>();
        for (var i = 0; i < TotalRows; i++)
        {
            var inField = random.NextDouble() < 1.0 / 3;
            ids[i] = Guid.NewGuid();
            groups[i] = inField
                ? itGroups[random.Next(itGroups.Count)]
                : otherGroups[random.Next(otherGroups.Count)];
            var roll = random.NextDouble();
            var noun = i % 3 == 0 ? "utvecklare" : "medarbetare";
            titles[i] = roll < 0.2 ? $"Junior {noun} {i}"
                : roll < 0.25 ? $"Trainee {noun} {i}"
                : roll < 0.3 ? $"Student {noun} {i}"
                : $"System{noun} {i}";
            descriptions[i] = $"Beskrivning av tjänsten nummer {i}";
            externalIds[i] = $"lat-{i}";
            if (classifier.Classify(groups[i], new JuniorClassificationInput(externalIds[i], titles[i], descriptions[i], null))
                is { } classification)
                assessed.Add((ids[i], classification));
        }

        await using (var connection = new NpgsqlConnection(db.Database.GetConnectionString()))
        {
            await connection.OpenAsync(ct);
            await using var insert = new NpgsqlCommand(
                "INSERT INTO job_ads "
                + "(id, title, description, url, published_at, created_at, status, source, company_name, remote, "
                + "occupation_group_concept_id, external_source, external_id) "
                + "SELECT u.id, u.title, u.description, "
                + "'https://example.com/jobb/' || u.i, "
                + "now() - (((u.i * 7919) % " + TotalRows + ") || ' minutes')::interval, now(), "
                + "CASE WHEN u.i % 5 = 0 THEN 'Archived' ELSE 'Active' END, 'Platsbanken', "
                + "'Arbetsgivare ' || (u.i % 997) || ' AB', false, u.grp, 'Platsbanken', u.ext "
                + "FROM unnest(@rowIds, @groups, @titles, @descriptions, @ids) WITH ORDINALITY "
                + "AS u(id, grp, title, description, ext, i);",
                connection)
            {
                CommandTimeout = 600,
            };
            insert.Parameters.AddWithValue("rowIds", ids);
            insert.Parameters.AddWithValue("groups", groups);
            insert.Parameters.AddWithValue("titles", titles);
            insert.Parameters.AddWithValue("descriptions", descriptions);
            insert.Parameters.AddWithValue("ids", externalIds);
            await insert.ExecuteNonQueryAsync(ct);

            await using var assess = new NpgsqlCommand(
                "INSERT INTO job_ad_junior_assessments "
                + "(job_ad_id, verdict, rule_id, rule_set_version, classified_fingerprint, classified_at) "
                + "SELECT a.id, a.verdict, a.rule, a.version, NULL, now() "
                + "FROM unnest(@assessedIds, @verdicts, @rules, @versions) AS a(id, verdict, rule, version);",
                connection)
            {
                CommandTimeout = 600,
            };
            assess.Parameters.AddWithValue("assessedIds", assessed.Select(a => a.Id).ToArray());
            assess.Parameters.AddWithValue("verdicts", assessed.Select(a => a.Classification.Verdict.ToString()).ToArray());
            assess.Parameters.AddWithValue("rules", assessed.Select(a => a.Classification.RuleId).ToArray());
            assess.Parameters.AddWithValue("versions", assessed.Select(a => a.Classification.RuleSetVersion).ToArray());
            await assess.ExecuteNonQueryAsync(ct);
        }

        await db.Database.ExecuteSqlRawAsync("VACUUM (ANALYZE) job_ads;", ct);
        await db.Database.ExecuteSqlRawAsync("VACUUM (ANALYZE) job_ad_junior_assessments;", ct);
        return assessed.GroupBy(a => a.Classification.Verdict).ToDictionary(g => g.Key, g => g.Count());
    }
}
