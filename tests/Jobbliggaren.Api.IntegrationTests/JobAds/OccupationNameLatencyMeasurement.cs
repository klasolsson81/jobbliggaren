using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Internal;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// #2199 — the latency of the occupation-name filter and its per-variant counts against ADR 0045 class
/// (a), read/list: <b>p95 300 ms</b> (Klas-locked 2026-05-17), measured through the shared search port.
/// <para>
/// <b>Why this corpus.</b> The cost is the Yrke union over <c>ssyk_concept_id</c> and the GROUP BY of
/// (name, group), so the ads carry real occupation-names and groups from the committed lineage, about a
/// quarter of them deprecated as production's are (23.8% measured 2026-10-24), skewed so a few names
/// hold most ads. The dedicated container (#1013) owns the table and its statistics.
/// </para>
/// <para>
/// ⚠ <b>OBSERVE-ONLY</b> (ADR 0045, as <see cref="ExcludedTermLatencyMeasurement"/>): durations are written
/// to test output and read into the PR body. What is asserted is that each search found ads, and that
/// every variant's count equals the total its pick lists (ADR 0120: a rendered count is exact).
/// </para>
/// <para>
/// <b>Result, measured 2026-10-24 against this class at 40 000 ads (32 000 active), 24.0% coded with a
/// deprecated name.</b> A dated reading of a finished run on a developer machine, not a live number:
/// re-run this class to regenerate it.
/// <code>
/// search                                     ads/keys   p50       p95
/// list+count (no Yrke)                       32 000     7.9 ms    11.8 ms
/// list+count 1 group                          2 121     3.4 ms     3.9 ms
/// list+count 1 name                           1 928     3.3 ms     4.0 ms
/// list+count 5 names                          3 254     5.2 ms     5.9 ms
/// list+count 400 names (681 names, 33 arms)  15 536    19.3 ms    28.8 ms
/// list+count 1 name + 1 group                 2 121     3.7 ms     4.8 ms
/// list+count 5 names, q = "utvecklare"        1 104     8.3 ms    10.2 ms
/// group facet, no filter                        400    12.0 ms    16.3 ms
/// name facet, no filter                       2 184    25.6 ms    37.0 ms
/// name facet, 5 names ticked                  2 184    25.5 ms    45.7 ms
/// name facet, q = "utvecklare"                1 973    19.3 ms    24.6 ms
/// </code>
/// The name arm's count reads <c>ix_job_ads_ssyk_concept_id</c> through a bitmap index scan.
/// </para>
/// </summary>
[Collection("JobAdBrowsePlan")]
// Same trait as the collection siblings: it excludes nothing from a default run (AGENTS.md §7); it marks
// that this class TRUNCATEs, seeds a corpus and drives a few hundred port calls.
[Trait("Category", "SmokeTest")]
public class OccupationNameLatencyMeasurement(JobAdBrowsePlanFixture fixture, ITestOutputHelper output)
{
    private const int TotalRows = 40_000;
    private const int Iterations = 30;
    private const string ResourceName = "Jobbliggaren.Infrastructure.Taxonomy.occupation-name-lineage.json";

    private readonly JobAdBrowsePlanFixture _fixture = fixture;
    private readonly ITestOutputHelper _output = output;

    [Fact]
    public async Task OccupationNames_LatencyAgainstAdr0045ClassA_AndExactCounts()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = _fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var lineageProvider = scope.ServiceProvider.GetRequiredService<IOccupationNameLineageProvider>();
        var lineage = lineageProvider.Lineage;
        // Built by hand as ExcludedTermLatencyMeasurement builds it: the fixture composes persistence
        // only. No search here carries a minus-word, so the label matcher is never asked.
        var search = new JobAdSearchQuery(db, new JobAdSearchTextResolver(
            new SearchQueryParser(),
            new OccupationSynonymExpander(Options.Create(new SearchSynonymsOptions())),
            Substitute.For<ITaxonomyLabelMatcher>(),
            lineageProvider));

        var (current, deprecated) = ReadNames();
        var seeding = Stopwatch.StartNew();
        var seeded = await SeedAsync(db, current, deprecated, ct);
        _output.WriteLine($"Seeded {TotalRows:N0} ads in {seeding.Elapsed.TotalSeconds:F0} s; "
            + $"{seeded.DeprecatedShare:P1} carry a deprecated name.");

        // The names and the group the corpus uses most, so every search below reaches ads.
        var frequent = seeded.FrequentCurrentNames;
        var one = frequent.Take(1).ToList();
        var five = frequent.Take(5).ToList();
        var fourHundred = current.Take(SearchCriteriaMax).Select(n => n.Id).ToList();
        var group = seeded.FrequentGroup;
        _output.WriteLine($"400 picks expand to {lineage.Expand(fourHundred).ConceptIds.Count} names "
            + $"and {lineage.Expand(fourHundred).SameGroupOnly.Count} same-group arms.");

        var scenarios = new (string Label, JobAdFilterCriteria Filter)[]
        {
            ("(no Yrke)", Filter()),
            ("1 group", Filter(groups: [group])),
            ("1 name", Filter(names: one)),
            ("5 names", Filter(names: five)),
            ("400 names", Filter(names: fourHundred)),
            ("1 name + 1 group", Filter(groups: [group], names: one)),
            ("5 names, q = \"utvecklare\"", Filter(names: five, q: "utvecklare")),
        };

        foreach (var (label, filter) in scenarios)
        {
            var total = (await search.SearchAsync(Page(filter), ct)).TotalCount;
            total.ShouldBeGreaterThan(0, $"{label}: a search that finds nothing times nothing");
            var (p50, p95) = await MeasureAsync(() => search.SearchAsync(Page(filter), ct).AsTask(), ct);
            _output.WriteLine($"list+count  {label,-30} {total,6} ads  p50 {p50,6:F1} ms  p95 {p95,6:F1} ms");
        }

        foreach (var (label, filter, dimension) in new[]
                 {
                     ("group facet, no filter", Filter(), FacetDimension.OccupationGroup),
                     ("name facet, no filter", Filter(), FacetDimension.OccupationName),
                     ("name facet, 5 names ticked", Filter(names: five), FacetDimension.OccupationName),
                     ("name facet, q = \"utvecklare\"", Filter(q: "utvecklare"), FacetDimension.OccupationName),
                 })
        {
            var counts = await search.FacetCountsAsync(filter, dimension, ct);
            counts.ShouldNotBeEmpty(label);
            var (p50, p95) = await MeasureAsync(() => search.FacetCountsAsync(filter, dimension, ct).AsTask(), ct);
            _output.WriteLine($"facet       {label,-30} {counts.Count,6} keys p50 {p50,6:F1} ms  p95 {p95,6:F1} ms");
        }

        // ADR 0120 — each variant's count is the total its own pick lists.
        var nameCounts = await search.FacetCountsAsync(Filter(), FacetDimension.OccupationName, ct);
        foreach (var name in five)
            nameCounts.GetValueOrDefault(name).ShouldBe(await search.CountAsync(Filter(names: [name]), ct), name);

        _output.WriteLine(await ExplainNameCountAsync(db, lineage.Expand(five), ct));
    }

    private const int SearchCriteriaMax = 400;

    private static JobAdFilterCriteria Filter(
        IReadOnlyList<string>? groups = null, IReadOnlyList<string>? names = null, string? q = null) => new(
        OccupationGroup: groups ?? [],
        OccupationName: names ?? [],
        Municipality: [],
        Region: [],
        EmploymentType: [],
        WorktimeExtent: [],
        Employer: [],
        Remote: false,
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

    // The shape of the count for five picks: whether the planner reaches ix_job_ads_ssyk_concept_id
    // for the name arm and ORs it with the group arm.
    private static async Task<string> ExplainNameCountAsync(
        AppDbContext db, OccupationNameExpansion expansion, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(db.Database.GetConnectionString());
        await connection.OpenAsync(ct);
        await using var explain = new NpgsqlCommand(
            "EXPLAIN SELECT count(*) FROM job_ads AS j WHERE j.status = 'Active' "
            + "AND j.ssyk_concept_id = ANY(@names)",
            connection);
        explain.Parameters.AddWithValue("names", expansion.ConceptIds.ToArray());
        var plan = new StringBuilder("EXPLAIN, count for five picks:\n");
        await using var reader = await explain.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            plan.AppendLine(reader.GetString(0));
        return plan.ToString();
    }

    private static (List<Name> Current, List<Name> Deprecated) ReadNames()
    {
        using var stream = typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {ResourceName} is missing.");
        using var document = JsonDocument.Parse(stream);
        var names = document.RootElement.GetProperty("occupationNames").EnumerateArray()
            .Where(e => e.GetProperty("groupConceptId").ValueKind == JsonValueKind.String)
            .Select(e => new Name(
                e.GetProperty("conceptId").GetString()!,
                e.GetProperty("groupConceptId").GetString()!,
                e.GetProperty("currentIn").GetArrayLength() > 0))
            .ToList();
        return (names.Where(n => n.Current).ToList(), names.Where(n => !n.Current).ToList());
    }

    /// <summary>
    /// TRUNCATE-and-own, bulk-seed, then VACUUM (ANALYZE). Each ad draws a name with a cubic skew, so a few
    /// names hold most ads, and a quarter of the draws come from the deprecated names; each ad carries its
    /// name's own group, as JobTech's ads do. Titles carry "utvecklare" on every third ad for the q search.
    /// </summary>
    private static async Task<Seeded> SeedAsync(
        AppDbContext db, List<Name> current, List<Name> deprecated, CancellationToken ct)
    {
        db.Database.SetCommandTimeout(600);
        await db.Database.ExecuteSqlRawAsync("TRUNCATE job_ads;", ct);

        var random = new Random(2199);
        var names = new string[TotalRows];
        var groups = new string[TotalRows];
        var deprecatedDraws = 0;
        for (var i = 0; i < TotalRows; i++)
        {
            var fromDeprecated = random.NextDouble() < 0.24;
            var pool = fromDeprecated ? deprecated : current;
            var picked = pool[(int)(pool.Count * Math.Pow(random.NextDouble(), 3))];
            names[i] = picked.Id;
            groups[i] = picked.Group;
            deprecatedDraws += fromDeprecated ? 1 : 0;
        }

        await using (var connection = new NpgsqlConnection(db.Database.GetConnectionString()))
        {
            await connection.OpenAsync(ct);
            await using var insert = new NpgsqlCommand(
                "INSERT INTO job_ads "
                + "(id, title, description, url, published_at, created_at, status, source, company_name, remote, "
                + "ssyk_concept_id, occupation_group_concept_id) "
                + "SELECT gen_random_uuid(), "
                + "CASE WHEN u.i % 3 = 0 THEN 'Systemutvecklare ' ELSE 'Medarbetare ' END || u.i, "
                + "'Beskrivning av tjänsten nummer ' || u.i, "
                + "'https://example.com/jobb/' || u.i, "
                + "now() - (((u.i * 7919) % " + TotalRows + ") || ' minutes')::interval, now(), "
                + "CASE WHEN u.i % 5 = 0 THEN 'Archived' ELSE 'Active' END, 'Platsbanken', "
                + "'Arbetsgivare ' || (u.i % 997) || ' AB', false, u.name, u.grp "
                + "FROM unnest(@names, @groups) WITH ORDINALITY AS u(name, grp, i);",
                connection)
            {
                CommandTimeout = 600,
            };
            insert.Parameters.AddWithValue("names", names);
            insert.Parameters.AddWithValue("groups", groups);
            await insert.ExecuteNonQueryAsync(ct);
        }

        await db.Database.ExecuteSqlRawAsync("VACUUM (ANALYZE) job_ads;", ct);

        var currentIds = current.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        var frequentNames = names.Where(currentIds.Contains)
            .GroupBy(n => n, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Key).Take(5).ToList();
        var frequentGroup = groups.GroupBy(g => g, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).First().Key;
        return new Seeded(frequentNames, frequentGroup, (double)deprecatedDraws / TotalRows);
    }

    private sealed record Name(string Id, string Group, bool Current);

    private sealed record Seeded(IReadOnlyList<string> FrequentCurrentNames, string FrequentGroup, double DeprecatedShare);
}
