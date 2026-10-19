using System.Data.Common;
using System.Diagnostics;
using Jobbliggaren.Api.IntegrationTests.JobAds;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.CompanyWatches.Queries.GetCriterionNewAdCounts;
using Jobbliggaren.Application.CompanyWatches.Queries.ListNewCriterionAds;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries.GetTaxonomyTree;
using Jobbliggaren.Application.Matching.Profiles;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Infrastructure.Taxonomy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.CompanyWatches;

/// <summary>
/// ADR 0175 D7 (#1663 PR 2) — the latency of the Branschbevakning count and page against ADR 0045 class (a),
/// read/list: <b>p95 300 ms</b> (Klas-locked 2026-05-17), measured as handler latency. The same run supplies the
/// per-request cost the three rate-limit buckets are derived from
/// (<c>RateLimitingOptions.CriterionNewAdCounts</c>, <c>CriterionNewAds</c>, <c>CriterionNewAdsSeen</c>).
///
/// <para>
/// One user at <see cref="CompanyWatchCriterion.MaxPerUser"/> criteria, each last scanned and each
/// holding <see cref="UnseenHitsPerCriterion"/> unseen hits on Active ads, plus hits on Archived ads the next night's
/// purge has not yet removed, over a <c>job_ads</c> table of production's order of size with wide rows. Nothing caps
/// a criterion's hits but its member companies' Active ads (ADR 0175 D12), so the per-criterion figure is a stated
/// order of magnitude, not a bound. The page reads one criterion at its cap and grades its window with
/// security-auditor S-5's ceiling profile through the real grading port.
/// </para>
///
/// <para>
/// ⚠ <b>OBSERVE-ONLY</b> (ADR 0045): durations are written to test output and read into the dated report. What is
/// asserted is that the seeded state is the one being measured — every criterion counts its hits and the page cuts
/// at its cap.
/// </para>
/// </summary>
[Collection("JobAdBrowsePlan")]
[Trait("Category", "SmokeTest")]
public class CriterionNewAdsLatencyMeasurement(JobAdBrowsePlanFixture fixture, ITestOutputHelper output)
{
    private const int UnseenHitsPerCriterion = 2_500;
    private const int ArchivedHitsPerCriterion = 500;
    private const int TotalAds = 43_000;
    private const int ActiveAds = TotalAds - ArchivedHitsPerCriterion;
    private const int CeilingOccupationGroups = 391;
    private const int Iterations = 40;
    private const string SeedMarker = "cw-criterion-latency-";

    private static readonly DateTimeOffset T0 = DateTimeOffset.UtcNow.AddDays(-10);

    [Fact]
    public async Task The_count_and_the_page_against_Adr0045_class_a()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = Guid.NewGuid();
        var seeding = Stopwatch.StartNew();

        var scopeFactory = fixture.Services.GetRequiredService<IServiceScopeFactory>();
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Test");
        await new TaxonomySnapshotSeeder(scopeFactory, environment, NullLogger<TaxonomySnapshotSeeder>.Instance)
            .StartAsync(ct);
        var taxonomy = new TaxonomyReadModel(scopeFactory);
        var tree = await taxonomy.GetTreeAsync(ct);
        var groups = tree.OccupationFields
            .SelectMany(f => f.OccupationGroups)
            .Select(g => g.ConceptId)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .Take(CeilingOccupationGroups)
            .ToArray();

        var criteria = await SeedAsync(db, userId, tree, groups, ct);

        var clock = new FixedClock(T0);
        var seeker = JobSeeker.Register(userId, TermsAcceptance.AcceptCurrent(clock), clock).Value;
        seeker.UpdateMatchPreferences(
            MatchPreferences.Create(
                groups,
                tree.Regions.Select(r => r.ConceptId),
                null,
                tree.Regions.SelectMany(r => r.Municipalities).Select(m => m.ConceptId)).Value,
            clock);
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);
        output.WriteLine(
            $"Seeded {TotalAds:N0} ads, {criteria.Count} criteria × ({UnseenHitsPerCriterion:N0} unseen + "
            + $"{ArchivedHitsPerCriterion:N0} archived) hits in {seeding.Elapsed.TotalSeconds:F0} s.");

        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(userId);
        var counts = new GetCriterionNewAdCountsQueryHandler(db, user);

        // Non-vacuity: each criterion counts exactly its unseen Active hits, and the page cuts at its cap.
        CriterionNewAdCountsDto answer;
        CapturedCommand countStatement;
        using (var capture = new CommandCapture(db))
        {
            answer = await counts.Handle(new GetCriterionNewAdCountsQuery(), ct);
            countStatement = capture.Commands.ShouldHaveSingleItem();
        }
        answer.Items.Count.ShouldBe(CompanyWatchCriterion.MaxPerUser);
        answer.Items.ShouldAllBe(i => i.NewAds == UnseenHitsPerCriterion);
        output.WriteLine("GET /new-ad-counts, EXPLAIN (ANALYZE, BUFFERS):");
        foreach (var line in await ExplainAsync(db, countStatement, ct))
            output.WriteLine(line);

        var profileBuilder = new MatchProfileBuilder(db, user, taxonomy);
        var profile = (await profileBuilder.BuildFullForSortAsync(ct)).Fast;
        profile.SsykGroupConceptIds.Count.ShouldBe(CeilingOccupationGroups);
        profile.PreferredRegionConceptIds.Count.ShouldBe(tree.Regions.Count);
        profile.PreferredMunicipalityConceptIds.Count.ShouldBe(tree.Regions.Sum(r => r.Municipalities.Count));
        output.WriteLine(
            $"Profile: {profile.SsykGroupConceptIds.Count} occupation groups, {profile.PreferredRegionConceptIds.Count} "
            + $"regions, {profile.PreferredMunicipalityConceptIds.Count} municipalities, "
            + $"{profile.PreferredEmploymentTypeConceptIds.Count} employment types.");

        var expander = scope.ServiceProvider.GetRequiredService<IOccupationSynonymExpander>();
        var grading = new PerUserJobAdSearchQuery(
            db, SearchTextResolvers.For(scope, expander), new JobAdSearchQuery(db, SearchTextResolvers.For(scope, expander)));
        var page = new ListNewCriterionAdsQueryHandler(
            db, user, Substitute.For<IFailedAccessLogger>(), profileBuilder, grading);
        var query = new ListNewCriterionAdsQuery(criteria[0].Value);

        var graded = (await page.Handle(query, ct))!;
        graded.Rows.Count.ShouldBe(ListNewCriterionAdsQueryHandler.MaxRows);
        graded.Truncated.ShouldBeTrue();
        graded.MatchingAssessed.ShouldBeTrue();
        graded.Rows.ShouldContain(r => r.MatchesYou == true);

        output.WriteLine($"ADR 0045 class (a): p95 300 ms. {Iterations} warmed iterations each, nearest-rank p95.");
        var (countP50, countP95) = await MeasureAsync(() => counts.Handle(new GetCriterionNewAdCountsQuery(), ct).AsTask(), ct);
        output.WriteLine($"GET /new-ad-counts, 20 criteria                         p50 {countP50,6:F1} ms  p95 {countP95,6:F1} ms");

        var (pageP50, pageP95) = await MeasureAsync(() => page.Handle(query, ct).AsTask(), ct);
        output.WriteLine($"GET /{{id}}/new-ads, cap {ListNewCriterionAdsQueryHandler.MaxRows}, ceiling profile graded   p50 {pageP50,6:F1} ms  p95 {pageP95,6:F1} ms");

        // Reverse order, the 2026-09-07 protocol's control for order effects.
        var (pageAgainP50, pageAgainP95) = await MeasureAsync(() => page.Handle(query, ct).AsTask(), ct);
        var (countAgainP50, countAgainP95) = await MeasureAsync(
            () => counts.Handle(new GetCriterionNewAdCountsQuery(), ct).AsTask(), ct);
        output.WriteLine($"Control pass: page p50 {pageAgainP50:F1} / p95 {pageAgainP95:F1} ms; count p50 {countAgainP50:F1} / p95 {countAgainP95:F1} ms");

        seeker.UpdateMatchPreferences(MatchPreferences.Empty, clock);
        await db.SaveChangesAsync(ct);
        var (bareP50, bareP95) = await MeasureAsync(() => page.Handle(query, ct).AsTask(), ct);
        output.WriteLine($"GET /{{id}}/new-ads, cap {ListNewCriterionAdsQueryHandler.MaxRows}, no stated occupation     p50 {bareP50,6:F1} ms  p95 {bareP95,6:F1} ms");
    }

    private static async Task<(double P50, double P95)> MeasureAsync(Func<Task> call, CancellationToken ct)
    {
        for (var i = 0; i < 5; i++)
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

    private static async Task<List<string>> ExplainAsync(AppDbContext db, CapturedCommand statement, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var explain = new NpgsqlCommand(
                "EXPLAIN (ANALYZE, BUFFERS) " + statement.Sql, (NpgsqlConnection)db.Database.GetDbConnection());
            explain.Parameters.AddRange(statement.Parameters);
            var plan = new List<string>();
            await using var reader = await explain.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                plan.Add(reader.GetString(0));
            return plan;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private sealed record CapturedCommand(string Sql, NpgsqlParameter[] Parameters);

    /// <summary>The statements EF sends for one context while the capture is open, read from its diagnostic events.</summary>
    private sealed class CommandCapture
        : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly DbContext _context;
        private readonly List<IDisposable> _subscriptions = [];
        private readonly List<CapturedCommand> _commands = [];

        public CommandCapture(DbContext context)
        {
            _context = context;
            var all = DiagnosticListener.AllListeners.Subscribe(this);
            lock (_subscriptions)
                _subscriptions.Add(all);
        }

        public IReadOnlyList<CapturedCommand> Commands
        {
            get
            {
                lock (_commands)
                    return [.. _commands];
            }
        }

        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name != DbLoggerCategory.Name)
                return;
            var subscription = listener.Subscribe(this);
            lock (_subscriptions)
                _subscriptions.Add(subscription);
        }

        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Key != RelationalEventId.CommandExecuting.Name
                || value.Value is not CommandEventData data
                || !ReferenceEquals(data.Context, _context))
                return;

            var parameters = data.Command.Parameters.Cast<DbParameter>()
                .Select(p => ((NpgsqlParameter)p).Clone())
                .ToArray();
            lock (_commands)
                _commands.Add(new CapturedCommand(data.Command.CommandText, parameters));
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void Dispose()
        {
            lock (_subscriptions)
            {
                foreach (var subscription in _subscriptions)
                    subscription.Dispose();
                _subscriptions.Clear();
            }
        }
    }

    /// <summary>
    /// TRUNCATE-and-own, then seed. The criteria and their scan state go through the domain (Create, RecordScan);
    /// the ads and the hits are bulk-inserted in the shapes the ingest and the scan write — an Active ad whose hit is
    /// stamped after the criterion's last change, and Archived ads whose hits the purge has not reached — because
    /// tens of thousands of rows through EF would measure the seed, not the read. Then VACUUM (ANALYZE), so the
    /// planner reads real statistics and no autovacuum lands mid-measurement.
    /// </summary>
    private static async Task<List<CompanyWatchCriterionId>> SeedAsync(
        AppDbContext db, Guid userId, TaxonomyTreeDto tree, string[] groups, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE company_watch_criterion_ad_hits, company_watch_criteria CASCADE; TRUNCATE job_ads CASCADE;", ct);

        var municipalities = tree.Regions
            .SelectMany(r => r.Municipalities.Select(m => (Municipality: m.ConceptId, Region: r.ConceptId)))
            .ToArray();
        var employmentTypes = tree.EmploymentTypes.Select(e => e.ConceptId).ToArray();
        var seedAds =
            "INSERT INTO job_ads ("
            + "id, title, company_name, description, url, source, external_source, external_id, "
            + "raw_payload, status, published_at, expires_at, created_at, remote, organization_number, "
            + "occupation_group_concept_id, region_concept_id, municipality_concept_id, employment_type_concept_id) "
            + "SELECT gen_random_uuid(), 'Systemutvecklare ' || i, 'Foretag ' || (i % 3000) || ' AB', "
            + "'Beskrivning ' || i || '. ' || repeat('Arbetsuppgifter, kvalifikationer och villkor for tjansten. ', 30), "
            + "'https://example.com/jobb/' || i, 'Platsbanken', 'Platsbanken', "
            + $"'{SeedMarker}' || i, jsonb_build_object('id', '{SeedMarker}' || i), "
            + $"CASE WHEN i < {UnseenHitsPerCriterion} OR i >= {UnseenHitsPerCriterion + ArchivedHitsPerCriterion} "
            + "THEN 'Active' ELSE 'Archived' END, "
            + "now() - (i || ' minutes')::interval, now() + interval '60 days', now() - interval '2 days', false, "
            + "lpad((5520000000 + i)::text, 10, '0'), "
            + $"(@groups::text[])[1 + i % {groups.Length}], (@regions::text[])[1 + i % {municipalities.Length}], "
            + $"(@municipalities::text[])[1 + i % {municipalities.Length}], "
            + $"(@employmentTypes::text[])[1 + i % {employmentTypes.Length}] "
            + $"FROM generate_series(0, {TotalAds - 1}) AS i;";
        await db.Database.ExecuteSqlRawAsync(
            seedAds,
            [
                new NpgsqlParameter("groups", groups),
                new NpgsqlParameter("regions", municipalities.Select(m => m.Region).ToArray()),
                new NpgsqlParameter("municipalities", municipalities.Select(m => m.Municipality).ToArray()),
                new NpgsqlParameter("employmentTypes", employmentTypes),
            ],
            ct);

        var clock = new FixedClock(T0);
        var criteria = Enumerable.Range(0, CompanyWatchCriterion.MaxPerUser)
            .Select(_ =>
            {
                var c = CompanyWatchCriterion.Create(
                    userId, CompanyWatchCriteriaSpec.Create(["63910"], ["0180"]).Value, null, clock).Value;
                c.RecordScan(CompanyWatchCriterionScanOutcome.Scanned, T0.AddDays(9), new FixedClock(T0.AddDays(9)));
                return c;
            })
            .ToList();
        db.CompanyWatchCriteria.AddRange(criteria);
        await db.SaveChangesAsync(ct);

        // Criterion k holds a hit on every stride-th Active ad from the k-th, wrapping, so each criterion's hits
        // span the whole Active range and two criteria share an ad only where 20 × 2 500 exceeds the Active ads;
        // and one on each Archived ad. The stamps are distinct and after the criterion's creation, as the scan's
        // per-hit clock writes them.
        var pattern = SeedMarker + "%";
        var from = SeedMarker.Length + 1;
        var stride = ActiveAds / UnseenHitsPerCriterion;
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO company_watch_criterion_ad_hits
                (id, user_id, job_ad_id, company_watch_criterion_id, created_at,
                 read_in_notification_feed, dismissed_from_notification_feed)
            SELECT gen_random_uuid(), h.user_id, j.id, h.criterion_id,
                   now() - interval '1 day' + (row_number() OVER () || ' milliseconds')::interval, false, false
            FROM (
                SELECT c.user_id, c.id AS criterion_id,
                       CASE WHEN a.n < {UnseenHitsPerCriterion} THEN a.n
                            ELSE a.n + {ArchivedHitsPerCriterion} END AS ad
                FROM (SELECT user_id, id, row_number() OVER (ORDER BY id) - 1 AS k
                      FROM company_watch_criteria WHERE user_id = {userId}) c
                CROSS JOIN generate_series(0, {UnseenHitsPerCriterion - 1}) AS m
                CROSS JOIN LATERAL (SELECT (c.k + {stride} * m) % {ActiveAds} AS n) a
                UNION ALL
                SELECT c.user_id, c.id, archived
                FROM company_watch_criteria c
                CROSS JOIN generate_series(
                    {UnseenHitsPerCriterion}, {UnseenHitsPerCriterion + ArchivedHitsPerCriterion - 1}) AS archived
                WHERE c.user_id = {userId}
            ) h
            JOIN (SELECT id, substring(external_id from {from})::int AS ad FROM job_ads
                  WHERE external_id LIKE {pattern}) j ON j.ad = h.ad
            """,
            ct);

        // One statement per command: several in one are sent as an implicit transaction, which VACUUM refuses.
        await db.Database.ExecuteSqlRawAsync("VACUUM (ANALYZE) job_ads;", ct);
        await db.Database.ExecuteSqlRawAsync("VACUUM (ANALYZE) company_watch_criteria;", ct);
        await db.Database.ExecuteSqlRawAsync("VACUUM (ANALYZE) company_watch_criterion_ad_hits;", ct);
        return criteria.Select(c => c.Id).ToList();
    }

    private sealed class FixedClock(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => now;
    }
}
