using System.Diagnostics;
using System.Text;
using Jobbliggaren.Application.JobAds.Junior;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// The corpus and the timing the junior list's latency measurements share (<see cref="JuniorItCountLatencyMeasurement"/>,
/// <see cref="JuniorItFloorLatencyMeasurement"/>): TRUNCATE-and-own, bulk-seed, then VACUUM (ANALYZE) both tables, in
/// <see cref="JuniorItFilterLatencyMeasurement"/>'s shape. Each ad also gets a kommun and its län from the snapshot (none
/// for one in twenty), and one in ten a deadline that has passed.
/// </summary>
internal static class JuniorLatencyCorpus
{
    public const int TotalRows = 40_000;

    private const int Iterations = 30;

    /// <param name="withOrganisationNumbers">Gives each of the corpus's 997 employers one synthetic organisation number,
    /// with none on one ad in a hundred, as the importer leaves a blank number (ADR 0182 R5). The count's measurement
    /// reads no number, so it seeds none.</param>
    public static async Task<Dictionary<JuniorVerdict, int>> SeedAsync(
        AppDbContext db,
        JuniorFieldClassifier classifier,
        List<string> itGroups,
        List<string> otherGroups,
        List<(string Region, string Municipality)> places,
        bool withOrganisationNumbers,
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

        var organisationNumber = withOrganisationNumbers
            ? "CASE WHEN u.i % 100 = 0 THEN NULL ELSE '556' || lpad((u.i % 997)::text, 7, '0') END"
            : "NULL";

        await using (var connection = new NpgsqlConnection(db.Database.GetConnectionString()))
        {
            await connection.OpenAsync(ct);
            await using var insert = new NpgsqlCommand(
                "INSERT INTO job_ads "
                + "(id, title, description, url, published_at, created_at, expires_at, status, source, company_name, remote, "
                + "occupation_group_concept_id, region_concept_id, municipality_concept_id, external_source, external_id, "
                + "organization_number) "
                + "SELECT u.id, u.title, u.description, "
                + "'https://example.com/jobb/' || u.i, "
                + "now() - (((u.i * 7919) % " + TotalRows + ") || ' minutes')::interval, now(), "
                + "CASE WHEN u.expired THEN now() - interval '1 day' ELSE NULL END, "
                + "CASE WHEN u.i % 5 = 0 THEN 'Archived' ELSE 'Active' END, 'Platsbanken', "
                + "'Arbetsgivare ' || (u.i % 997) || ' AB', false, u.grp, u.region, u.municipality, 'Platsbanken', u.ext, "
                + organisationNumber + " "
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

    /// <summary>Three warm-up calls, then the median and the 95th percentile over thirty timed ones, in milliseconds.</summary>
    public static async Task<(double P50, double P95)> MeasureAsync(Func<Task> call, CancellationToken ct)
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

    /// <summary>The statement as EF sends it, under <c>EXPLAIN (ANALYZE, BUFFERS)</c>.</summary>
    public static async Task<string> ExplainAsync<T>(AppDbContext db, IQueryable<T> query, string label, CancellationToken ct)
    {
        await using var command = query.CreateDbCommand();
        command.CommandText = "EXPLAIN (ANALYZE, BUFFERS) " + command.CommandText;
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var plan = new StringBuilder($"EXPLAIN (ANALYZE, BUFFERS), {label}:\n");
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

    private static double Percentile(List<double> sorted, double p) =>
        sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Count) - 1, 0, sorted.Count - 1)];
}
