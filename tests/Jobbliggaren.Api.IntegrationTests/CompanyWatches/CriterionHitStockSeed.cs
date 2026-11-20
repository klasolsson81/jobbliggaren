using Jobbliggaren.Application.JobAds.Queries.GetTaxonomyTree;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Jobbliggaren.Api.IntegrationTests.CompanyWatches;

/// <summary>
/// The Branschbevakning layer both latency harnesses measure on: <c>CriterionNewAdsLatencyMeasurement</c> (#1663 PR 2: the
/// pill, <c>/nya</c> and its seen write) and <c>NotificationFeedLatencyMeasurement</c> (#1663 PR 3: the bell). One seed, two
/// consumers, so a raised fixture raises both, and the derivations that cite it describe the fixture the harness seeds.
///
/// <para>
/// <b>The size is fixed by a reading, not by a result.</b> PR 2 seeded 42 500 Active ads. S-2 read 45 260 Active ads on the
/// box on 2026-11-20 (release sequence 1681), which fired the recompute trigger of PR 2's three buckets; before any harness
/// ran, the fixture was set to <see cref="ActiveAds"/> = 50 000 (about 10 % headroom) under security-auditor's rule (v) and
/// the CTO's ruling of that day (<c>docs/reviews/2026-11-20-1663-pr3-s2-reading.md</c>).
/// </para>
/// </summary>
internal static class CriterionHitStockSeed
{
    internal const int UnseenHitsPerCriterion = 2_500;
    internal const int ArchivedHitsPerCriterion = 500;
    internal const int ActiveAds = 50_000;
    internal const int TotalAds = ActiveAds + ArchivedHitsPerCriterion;
    internal const string SeedMarker = "cw-criterion-latency-";

    /// <summary>
    /// TRUNCATE-and-own, then seed. The criteria and their scan state go through the domain (Create, RecordScan); the
    /// ads and the hits are bulk-inserted in the shapes the ingest and the scan write — an Active ad whose hit is stamped
    /// after the criterion's last change, and Archived ads whose hits the purge has not reached — because tens of
    /// thousands of rows through EF would measure the seed, not the read. Then VACUUM (ANALYZE), so the planner reads real
    /// statistics and no autovacuum lands mid-measurement. The bulk statements outlast Npgsql's 30 s default at this size,
    /// so the seed runs under its own command timeout and hands the caller's back.
    /// </summary>
    internal static async Task<List<CompanyWatchCriterionId>> SeedAsync(
        AppDbContext db, Guid userId, TaxonomyTreeDto tree, string[] groups, DateTimeOffset t0, CancellationToken ct)
    {
        var callersTimeout = db.Database.GetCommandTimeout();
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
        try
        {
            return await SeedUnderTimeoutAsync(db, userId, tree, groups, t0, ct);
        }
        finally
        {
            db.Database.SetCommandTimeout(callersTimeout);
        }
    }

    private static async Task<List<CompanyWatchCriterionId>> SeedUnderTimeoutAsync(
        AppDbContext db, Guid userId, TaxonomyTreeDto tree, string[] groups, DateTimeOffset t0, CancellationToken ct)
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

        var clock = new FixedClock(t0);
        var criteria = Enumerable.Range(0, CompanyWatchCriterion.MaxPerUser)
            .Select(_ =>
            {
                var c = CompanyWatchCriterion.Create(
                    userId, CompanyWatchCriteriaSpec.Create(["63910"], ["0180"]).Value, null, clock).Value;
                c.RecordScan(CompanyWatchCriterionScanOutcome.Scanned, t0.AddDays(9), new FixedClock(t0.AddDays(9)));
                return c;
            })
            .ToList();
        db.CompanyWatchCriteria.AddRange(criteria);
        await db.SaveChangesAsync(ct);

        // Criterion k holds a hit on every stride-th Active ad from the k-th, wrapping, so each criterion's hits
        // span the whole Active range and two criteria share an ad only where 20 × 2 500 exceeds the Active ads (at
        // 50 000 they share none, and every Active ad holds exactly one hit); and one on each Archived ad. The stamps
        // are distinct, in ad order, and after the criterion's creation, as the scan's per-hit clock writes them.
        var pattern = SeedMarker + "%";
        var from = SeedMarker.Length + 1;
        var stride = ActiveAds / UnseenHitsPerCriterion;
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO company_watch_criterion_ad_hits
                (id, user_id, job_ad_id, company_watch_criterion_id, created_at,
                 read_in_notification_feed, dismissed_from_notification_feed)
            SELECT gen_random_uuid(), h.user_id, j.id, h.criterion_id,
                   now() - interval '1 day' + (row_number() OVER (ORDER BY h.ad, h.criterion_id) || ' milliseconds')::interval,
                   false, false
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

    internal sealed class FixedClock(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => now;
    }
}
