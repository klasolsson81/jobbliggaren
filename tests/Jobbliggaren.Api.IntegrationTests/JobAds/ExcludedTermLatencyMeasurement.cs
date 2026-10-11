using System.Diagnostics;
using System.Text;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Internal;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// #2143 (ADR 0167) — the latency of the minus-word exclusion against ADR 0045 class (a), read/list:
/// <b>p95 300 ms</b> (Klas-locked 2026-05-17), measured as handler latency through the shared search
/// port, list and count together.
/// <para>
/// <b>Why this corpus.</b> The exclusion's cost is reading <c>search_vector</c>, so the descriptions are
/// sized and randomised to be stored out of line (TOAST) as production's are; short seeded descriptions
/// would leave the detoast cost out of the number. The vocabulary is placed at known rates so the
/// exclusions remove known shares of the corpus. The dedicated container (#1013) owns the table and its
/// statistics, so the numbers describe the query, not the run order.
/// </para>
/// <para>
/// ⚠ <b>OBSERVE-ONLY</b> (ADR 0045, as <see cref="SuggestUnionLatencyMeasurement"/>): durations are written
/// to test output and read into the PR body; what is asserted is that each exclusion actually removed
/// ads, and the plan the count of a search of only minus-words runs under production's GUC.
/// </para>
/// <para>
/// <b>Result, measured 2026-10-11 against this class at 40 000 ads (32 000 active).</b> A dated reading of
/// a finished run on a developer machine, not a live number: re-run this class to regenerate it.
/// <code>
/// q                                            p50       p95
/// (none)                                      7.5 ms    8.5 ms
/// "stockholm"            (plus-word baseline) 197 ms   228 ms
/// "-stockholm"                                18 ms     24 ms
/// "lärare -deltid"                            199 ms   266 ms   the plus-word "lärare" dominates
/// "-are"                 (excludes every ad)  72 ms     84 ms
/// five minus-words                            37 ms     45 ms
/// </code>
/// Two changes in the same PR made those numbers: the count's nested-loop prohibition (BitmapPlanCount;
/// five minus-words read 458 ms p95 without it, three of them matching nothing and each probed per row)
/// and the full-text arm placed last in the match (JobAdSearchComposition.MatchesText; "-are" read 663 ms
/// p95 with the arm first, because every row's search_vector was detoasted before its title was tested).
/// </para>
/// </summary>
[Collection("JobAdBrowsePlan")]
// Same trait as the collection siblings: it excludes nothing from a default run (AGENTS.md §7); it marks
// that this class TRUNCATEs, seeds a corpus and drives a few hundred port calls.
[Trait("Category", "SmokeTest")]
public class ExcludedTermLatencyMeasurement(JobAdBrowsePlanFixture fixture, ITestOutputHelper output)
{
    private const int TotalRows = 40_000;
    private const int Iterations = 30;

    private readonly JobAdBrowsePlanFixture _fixture = fixture;
    private readonly ITestOutputHelper _output = output;

    [Fact]
    public async Task MinusWords_LatencyAgainstAdr0045ClassA_AndTheirCountPlan()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = _fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seeding = Stopwatch.StartNew();
        await SeedAsync(db, ct);
        _output.WriteLine($"Seeded {TotalRows:N0} ads in {seeding.Elapsed.TotalSeconds:F0} s.");

        var sut = new JobAdSearchQuery(
            db,
            new JobAdSearchTextResolver(
                new SearchQueryParser(),
                new OccupationSynonymExpander(Options.Create(new SearchSynonymsOptions())),
                scope.ServiceProvider.GetRequiredService<ITaxonomyLabelMatcher>()));

        // Non-vacuity first: an exclusion that removed nothing would time a predicate that never ran.
        var active = await sut.CountAsync(Filter(null), ct);
        (await sut.CountAsync(Filter("-deltid"), ct)).ShouldBeLessThan(active);
        (await sut.CountAsync(Filter("lärare -deltid"), ct)).ShouldBeLessThan(await sut.CountAsync(Filter("lärare"), ct));
        (await sut.CountAsync(Filter("-are"), ct)).ShouldBeLessThan(active / 10);

        var plan = await ExplainExclusionCountAsync(db, ct);
        _output.WriteLine("Count of \"-deltid\" under the GUCs BitmapPlanCount sets for it:");
        _output.WriteLine(plan);
        plan.ShouldContain("Anti Join", Case.Insensitive,
            $"the count of a search of only minus-words must hash the excluded set once. Plan:\n{plan}");
        plan.ShouldContain("ix_job_ads_search_vector", Case.Insensitive,
            $"the excluded set must come from the GIN index. Plan:\n{plan}");

        _output.WriteLine($"ADR 0045 class (a): p95 300 ms. {TotalRows:N0} ads, {active:N0} active, {Iterations} warmed iterations each.");
        foreach (var q in new[]
                 {
                     null, "stockholm", "-stockholm", "lärare -deltid", "-are",
                     "-deltid -stockholm -heltid -sommar -kock",
                 })
        {
            var (p50, p95) = await MeasureAsync(sut, Filter(q), ct);
            _output.WriteLine($"q = {(q is null ? "(none)" : $"\"{q}\""),-46} p50 {p50,6:F1} ms  p95 {p95,6:F1} ms");
        }
    }

    private static JobAdFilterCriteria Filter(string? q) =>
        new(
            OccupationGroup: [], Municipality: [], Region: [], EmploymentType: [], WorktimeExtent: [],
            Employer: [], Remote: false, Q: q);

    private static async Task<(double P50, double P95)> MeasureAsync(
        JobAdSearchQuery sut, JobAdFilterCriteria filter, CancellationToken ct)
    {
        var criteria = new JobAdSearchCriteria(filter, JobAdSortBy.PublishedAtDesc, Page: 1, PageSize: 20);
        for (var i = 0; i < 3; i++)
            await sut.SearchAsync(criteria, ct);

        var samples = new List<double>(Iterations);
        for (var i = 0; i < Iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            await sut.SearchAsync(criteria, ct);
            sw.Stop();
            samples.Add(sw.Elapsed.TotalMilliseconds);
        }

        samples.Sort();
        return (Percentile(samples, 0.50), Percentile(samples, 0.95));
    }

    private static double Percentile(List<double> sorted, double p) =>
        sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Count) - 1, 0, sorted.Count - 1)];

    // The shape ApplyFilter composes for a search of only "-deltid" (ListJobAdsExcludedTermsTests pins the
    // EF side), under the GUCs BitmapPlanCount sets for it.
    private static async Task<string> ExplainExclusionCountAsync(AppDbContext db, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(db.Database.GetConnectionString());
        await connection.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await using (var set = new NpgsqlCommand(
                         "SET LOCAL enable_seqscan = off; SET LOCAL enable_nestloop = off", connection, tx))
            await set.ExecuteNonQueryAsync(ct);

        await using var explain = new NpgsqlCommand(
            "EXPLAIN SELECT count(*) FROM job_ads AS j WHERE j.status = 'Active' AND NOT EXISTS ("
            + "SELECT 1 FROM job_ads AS x WHERE x.id = j.id AND "
            + "(lower(x.title) LIKE '%deltid%' OR lower(x.company_name) LIKE '%deltid%' "
            + "OR x.search_vector @@ websearch_to_tsquery('swedish', 'deltid')))",
            connection, tx);
        var plan = new StringBuilder();
        await using (var reader = await explain.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                plan.AppendLine(reader.GetString(0));
        }

        await tx.RollbackAsync(ct);
        return plan.ToString();
    }

    /// <summary>
    /// TRUNCATE-and-own, bulk-seed, then ANALYZE (TRUNCATE wipes the statistics the planner needs).
    /// Each description is ~2.3 KB of md5 words drawn from a 500-word vocabulary — hex does not compress
    /// below the TOAST threshold, and a bounded vocabulary keeps the GIN index the size real text gives
    /// it — plus the search vocabulary on phases that are never the Archived one (every 5th ad):
    /// "Lärare" titles 10 % of ads, "deltid" is in 15 % and in half of the Lärare ads, "stockholm" in 25 %.
    /// </summary>
    private static async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        db.Database.SetCommandTimeout(600);
        await db.Database.ExecuteSqlRawAsync("TRUNCATE job_ads;", ct);

        var bulk =
            "INSERT INTO job_ads "
            + "(id, title, description, url, published_at, created_at, status, source, company_name, remote) "
            + "SELECT gen_random_uuid(), "
            + "CASE WHEN i % 20 IN (7, 9) THEN 'Lärare ' ELSE 'Systemutvecklare ' END || i, "
            + "(SELECT string_agg(md5(((i * 31 + g * 17) % 500)::text), ' ') FROM generate_series(1, 70) AS g) "
            + "|| CASE WHEN i % 10 = 3 OR i % 20 = 7 THEN ' Tjänsten är på deltid.' ELSE '' END "
            + "|| CASE WHEN i % 4 = 1 THEN ' Placering Stockholm.' ELSE '' END, "
            + "'https://example.com/jobb/' || i, "
            + "now() - (((i * 7919) % " + TotalRows + ") || ' minutes')::interval, "
            + "now(), "
            + "CASE WHEN i % 5 = 0 THEN 'Archived' ELSE 'Active' END, "
            + "'Platsbanken', "
            + "'Arbetsgivare ' || (i % 997) || ' AB', "
            + "false "
            + "FROM generate_series(0, " + (TotalRows - 1) + ") AS i;";
        await db.Database.ExecuteSqlRawAsync(bulk, ct);

        await db.Database.ExecuteSqlRawAsync("ANALYZE job_ads;", ct);
    }
}
