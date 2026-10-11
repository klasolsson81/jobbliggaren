using System.Diagnostics;
using System.Text;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Internal;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.JobAds;
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
/// q                                                 p50       p95
/// (none)                                            5.6 ms    7.0 ms
/// "stockholm"                  (plus-word baseline) 5.6 ms    6.1 ms
/// "-stockholm"                                      26 ms     27 ms
/// "lärare -deltid"                                  14 ms     15 ms
/// "-are"                       (excludes every ad)  55 ms     67 ms
/// "svenska"                    (plus-word baseline) 8.6 ms    9.0 ms
/// "-svenska"                   (every active ad)    27 ms     32 ms
/// five minus-words                                  38 ms     43 ms
/// "-c++"                       (no trigram)         18 ms     19 ms
/// five minus-words with no letter (dropped)         4.9 ms    5.7 ms
/// CountNewSinceAsync, "-svenska", last hour         14 ms     14 ms
/// </code>
/// Three changes in the same PR shaped those numbers, each read before against this class: the count's
/// nested-loop prohibition (BitmapPlanCount; five minus-words read 458 ms p95 without it, three of them
/// matching nothing and each probed per row), the item read with minus-words under the same GUCs
/// (BitmapPlanCount.ReadWithExclusionPlanAsync; "-svenska"'s item read probed and detoasted every active
/// row, 253 ms, without it) and the full-text arm placed last in the match (JobAdSearchComposition.MatchesText;
/// "-are" read 663 ms p95 with the arm first). Those earlier readings were taken before the seed vacuumed.
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

        // The label arm runs as production's does only over a seeded taxonomy, and the fixture runs no
        // hosted services.
        var scopeFactory = _fixture.Services.GetRequiredService<IServiceScopeFactory>();
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Test");
        await new TaxonomySnapshotSeeder(scopeFactory, environment, NullLogger<TaxonomySnapshotSeeder>.Instance)
            .StartAsync(ct);
        var labelMatcher = new TaxonomyReadModel(scopeFactory);
        (await labelMatcher.MatchExactLabelsAsync(["deltid"], ct)).WorktimeExtent.ShouldNotBeEmpty();

        var resolver = new JobAdSearchTextResolver(
            new SearchQueryParser(),
            new OccupationSynonymExpander(Options.Create(new SearchSynonymsOptions())),
            labelMatcher);
        var sut = new JobAdSearchQuery(db, resolver);

        // Non-vacuity first: an exclusion that removed nothing would time a predicate that never ran.
        var active = await sut.CountAsync(Filter(null), ct);
        (await sut.CountAsync(Filter("-deltid"), ct)).ShouldBeLessThan(active);
        (await sut.CountAsync(Filter("lärare -deltid"), ct)).ShouldBeLessThan(await sut.CountAsync(Filter("lärare"), ct));
        (await sut.CountAsync(Filter("-are"), ct)).ShouldBeLessThan(active / 10);
        (await sut.CountAsync(Filter("deltid -i(-deltid"), ct)).ShouldBe(0);
        (await sut.CountAsync(Filter("-x(or(-deltid"), ct)).ShouldBe(active);

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
                     null, "stockholm", "-stockholm", "lärare -deltid", "-are", "svenska", "-svenska",
                     "-deltid -stockholm -heltid -sommar -kock", "-c++", "-!!! -??? -### -&&& -***",
                     "-i(-deltid", "-x(or(-deltid", "-x\"-deltid",
                     "-i(-deltid -x(or(-stockholm -i!-heltid -x\"-sommar -i<-kock",
                 })
        {
            var criteria = new JobAdSearchCriteria(Filter(q), JobAdSortBy.PublishedAtDesc, Page: 1, PageSize: 20);
            var (p50, p95) = await MeasureAsync(() => sut.SearchAsync(criteria, ct).AsTask(), ct);
            _output.WriteLine($"q = {(q is null ? "(none)" : $"\"{q}\""),-46} p50 {p50,6:F1} ms  p95 {p95,6:F1} ms");
        }

        // A narrow outer side under the same GUCs: the new-results count builds the whole excluded set
        // whatever the window holds.
        var since = DateTimeOffset.UtcNow.AddHours(-1);
        var (newP50, newP95) = await MeasureAsync(
            () => sut.CountNewSinceAsync(Filter("-svenska"), since, ct).AsTask(), ct);
        _output.WriteLine($"CountNewSinceAsync, q = \"-svenska\", last hour       p50 {newP50,6:F1} ms  p95 {newP95,6:F1} ms");

        var relevance = new JobAdSearchCriteria(Filter("lärare -deltid"), JobAdSortBy.Relevance, Page: 1, PageSize: 20);
        var (relP50, relP95) = await MeasureAsync(() => sut.SearchAsync(relevance, ct).AsTask(), ct);
        _output.WriteLine($"Relevance, q = \"lärare -deltid\"                  p50 {relP50,6:F1} ms  p95 {relP95,6:F1} ms");

        // A seeker with 200 saved ads, through the status-only per-user path.
        var seeker = new JobSeekerId(Guid.NewGuid());
        await db.Database.ExecuteSqlAsync(
            $"INSERT INTO saved_job_ads (id, job_seeker_id, job_ad_id, created_at) SELECT gen_random_uuid(), {seeker.Value}, id, now() FROM job_ads WHERE status = 'Active' ORDER BY id LIMIT 200",
            ct);
        var perUser = new PerUserJobAdSearchQuery(db, resolver, sut);
        var savedOnly = new JobAdStatusFilter(SavedOnly: true, AppliedOnly: false, HideApplied: false);
        var (stP50, stP95) = await MeasureAsync(
            () => perUser.SearchByStatusAsync(
                Filter("-deltid"), seeker, savedOnly, JobAdSortBy.PublishedAtDesc, page: 1, pageSize: 20, ct).AsTask(),
            ct);
        _output.WriteLine($"SearchByStatusAsync, 200 saved, q = \"-deltid\"       p50 {stP50,6:F1} ms  p95 {stP95,6:F1} ms");
    }

    private static JobAdFilterCriteria Filter(string? q) =>
        new(
            OccupationGroup: [], Municipality: [], Region: [], EmploymentType: [], WorktimeExtent: [],
            Employer: [], Remote: false, Q: q);

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
            + "OR x.search_vector @@ plainto_tsquery('swedish', 'deltid')))",
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
    /// TRUNCATE-and-own, bulk-seed, then VACUUM (ANALYZE): TRUNCATE wipes the statistics the planner needs,
    /// and without the vacuum autovacuum sets the visibility map at a moment of its own, mid-measurement,
    /// changing what a count reads from the heap.
    /// Each description is ~2.3 KB of md5 words drawn from a 500-word vocabulary — hex does not compress
    /// below the TOAST threshold, and a bounded vocabulary keeps the GIN index the size real text gives
    /// it — plus the search vocabulary on phases that are never the Archived one (every 5th ad):
    /// "Lärare" titles 10 % of ads, "deltid" is in 15 % and in half of the Lärare ads, "stockholm" in 25 %,
    /// and "svenska" — in descriptions only — in 90 %.
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
            + "|| CASE WHEN i % 4 = 1 THEN ' Placering Stockholm.' ELSE '' END "
            + "|| CASE WHEN i % 10 <> 0 THEN ' Svenska krävs.' ELSE '' END, "
            + "'https://example.com/jobb/' || i, "
            + "now() - (((i * 7919) % " + TotalRows + ") || ' minutes')::interval, "
            + "now(), "
            + "CASE WHEN i % 5 = 0 THEN 'Archived' ELSE 'Active' END, "
            + "'Platsbanken', "
            + "'Arbetsgivare ' || (i % 997) || ' AB', "
            + "false "
            + "FROM generate_series(0, " + (TotalRows - 1) + ") AS i;";
        await db.Database.ExecuteSqlRawAsync(bulk, ct);

        await db.Database.ExecuteSqlRawAsync("VACUUM (ANALYZE) job_ads;", ct);
    }
}
