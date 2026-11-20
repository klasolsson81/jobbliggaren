using System.Diagnostics;
using System.Text;
using Jobbliggaren.Api.IntegrationTests.Sessions;
using Jobbliggaren.Application.JobAds.Junior;
using Jobbliggaren.Application.JobAds.Publication;
using Jobbliggaren.Application.JobAds.Queries.GetJuniorItJobAdCount;
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
/// ADR 0188 (#2265) — the latency of the count behind the junior quick link in the /jobb hero, against ADR 0045 class
/// (a), read/list: <b>p95 300 ms</b>, and against the per-account database-time anchor the count's rate-limit bucket is
/// derived from (<c>RateLimitingOptions.JobAdJuniorItCount</c>). It times <see cref="GetJuniorItJobAdCountQueryHandler"/>
/// over the real <see cref="JuniorItJobAdQuery"/> and <see cref="JuniorFieldResolver"/>: the GROUP BY on the kommun under
/// <see cref="JobAdPublication.IsListed"/> and the EXISTS on the stored assessment (<see cref="JuniorMembership"/>).
/// <para>
/// <b>Why this corpus.</b> As <see cref="JuniorItFilterLatencyMeasurement"/>'s, plus what this statement reads and that
/// one does not: every ad carries a kommun from the real taxonomy snapshot (with its län), one in twenty has none, and one
/// in ten has a deadline that has passed, so the count's GROUP BY has the real number of groups and
/// <see cref="JobAdPublication.IsListed"/> excludes rows on all three of its conditions.
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
    private const int TotalRows = 40_000;
    private const int Iterations = 30;

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
        var verdicts = await SeedAsync(db, new JuniorFieldClassifier(fields), itGroups, otherGroups, places, ct);
        _output.WriteLine($"Seeded {TotalRows:N0} ads in {seeding.Elapsed.TotalSeconds:F0} s; {itGroups.Count} Data/IT "
            + $"groups; {places.Count} kommuner; assessments: {string.Join(", ", verdicts.Select(v => $"{v.Value} {v.Key}"))}.");

        var clock = new MutableFakeDateTimeProvider { UtcNow = DateTimeOffset.UtcNow };
        var handler = new GetJuniorItJobAdCountQueryHandler(new JuniorItJobAdQuery(db, catalog), clock);
        var count = (await handler.Handle(new GetJuniorItJobAdCountQuery(), ct)).Count;
        var listed = await db.JobAds.AsNoTracking().CountAsync(JobAdPublication.IsListed(clock.UtcNow), ct);
        count.ShouldBeGreaterThan(0, "a count of nothing times nothing");
        count.ShouldBeLessThan(listed, "the junior count is a strict subset of the listed ads");

        var (p50, p95) = await MeasureAsync(() => handler.Handle(new GetJuniorItJobAdCountQuery(), ct).AsTask(), ct);
        _output.WriteLine($"junior-it-count  {count,6} of {listed,6} listed  p50 {p50,6:F1} ms  p95 {p95,6:F1} ms");

        var groups = JuniorMembership.GroupsOf(fields);
        _output.WriteLine(await ExplainAsync(db, db.JobAds
            .AsNoTracking()
            .Where(JobAdPublication.IsListed(clock.UtcNow))
            .Where(JuniorMembership.Of(db.JuniorAssessments, groups))
            .GroupBy(j => j.MunicipalityConceptId)
            .Select(g => new { g.Key, Count = g.Count() }), ct));
    }

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

    private static async Task<string> ExplainAsync<T>(AppDbContext db, IQueryable<T> query, CancellationToken ct)
    {
        await using var command = query.CreateDbCommand();
        command.CommandText = "EXPLAIN (ANALYZE, BUFFERS) " + command.CommandText;
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var plan = new StringBuilder("EXPLAIN (ANALYZE, BUFFERS), junior-it-count:\n");
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
    /// TRUNCATE-and-own, bulk-seed, then VACUUM (ANALYZE) both tables, in <see cref="JuniorItFilterLatencyMeasurement"/>'s
    /// shape. Each ad also gets a kommun and its län from the snapshot (none for one in twenty), and one in ten a
    /// deadline that has passed.
    /// </summary>
    private static async Task<Dictionary<JuniorVerdict, int>> SeedAsync(
        AppDbContext db,
        JuniorFieldClassifier classifier,
        List<string> itGroups,
        List<string> otherGroups,
        List<(string Region, string Municipality)> places,
        CancellationToken ct)
    {
        db.Database.SetCommandTimeout(600);
        await db.Database.ExecuteSqlRawAsync("TRUNCATE job_ads CASCADE;", ct);

        var random = new Random(2265);
        var ids = new Guid[TotalRows];
        var groups = new string[TotalRows];
        var titles = new string[TotalRows];
        var descriptions = new string[TotalRows];
        var externalIds = new string[TotalRows];
        var regions = new string?[TotalRows];
        var municipalities = new string?[TotalRows];
        var expired = new bool[TotalRows];
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
            if (random.NextDouble() >= 0.05)
            {
                var place = places[random.Next(places.Count)];
                regions[i] = place.Region;
                municipalities[i] = place.Municipality;
            }
            expired[i] = random.NextDouble() < 0.1;
            if (classifier.Classify(groups[i], new JuniorClassificationInput(externalIds[i], titles[i], descriptions[i], null))
                is { } classification)
                assessed.Add((ids[i], classification));
        }

        await using (var connection = new NpgsqlConnection(db.Database.GetConnectionString()))
        {
            await connection.OpenAsync(ct);
            await using var insert = new NpgsqlCommand(
                "INSERT INTO job_ads "
                + "(id, title, description, url, published_at, created_at, expires_at, status, source, company_name, remote, "
                + "occupation_group_concept_id, region_concept_id, municipality_concept_id, external_source, external_id) "
                + "SELECT u.id, u.title, u.description, "
                + "'https://example.com/jobb/' || u.i, "
                + "now() - (((u.i * 7919) % " + TotalRows + ") || ' minutes')::interval, now(), "
                + "CASE WHEN u.expired THEN now() - interval '1 day' ELSE NULL END, "
                + "CASE WHEN u.i % 5 = 0 THEN 'Archived' ELSE 'Active' END, 'Platsbanken', "
                + "'Arbetsgivare ' || (u.i % 997) || ' AB', false, u.grp, u.region, u.municipality, 'Platsbanken', u.ext "
                + "FROM unnest(@rowIds, @groups, @titles, @descriptions, @ids, @regions, @municipalities, @expired) "
                + "WITH ORDINALITY AS u(id, grp, title, description, ext, region, municipality, expired, i);",
                connection)
            {
                CommandTimeout = 600,
            };
            insert.Parameters.AddWithValue("rowIds", ids);
            insert.Parameters.AddWithValue("groups", groups);
            insert.Parameters.AddWithValue("titles", titles);
            insert.Parameters.AddWithValue("descriptions", descriptions);
            insert.Parameters.AddWithValue("ids", externalIds);
            insert.Parameters.AddWithValue("regions", regions);
            insert.Parameters.AddWithValue("municipalities", municipalities);
            insert.Parameters.AddWithValue("expired", expired);
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
