using System.Diagnostics;
using Jobbliggaren.Api.IntegrationTests.JobAds;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.CompanyWatches.Queries.GetCriterionNewAdCounts;
using Jobbliggaren.Application.CompanyWatches.Queries.ListNewCriterionAds;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Infrastructure.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.CompanyWatches;

/// <summary>
/// ADR 0175 D7 (#1663 PR 2) — the latency of the Branschbevakning count and page against ADR 0045 class (a),
/// read/list: <b>p95 300 ms</b> (Klas-locked 2026-05-17), measured as handler latency at the ceiling the two reads can
/// meet. The same run supplies the per-request cost the three rate-limit buckets are derived from
/// (<c>RateLimitingOptions.CriterionNewAdCounts</c>, <c>CriterionNewAds</c>, <c>CriterionNewAdsSeen</c>).
///
/// <para>
/// <b>The ceiling.</b> One user at <see cref="CompanyWatchCriterion.MaxPerUser"/> criteria, each last scanned and each
/// holding <see cref="UnseenHitsPerCriterion"/> unseen hits on Active ads, plus hits on Archived ads the next night's
/// purge has not yet removed, over a <c>job_ads</c> table of production's order of size with wide rows. Nothing caps
/// a criterion's hits but its member companies' Active ads (ADR 0175 D12), so the per-criterion figure is a stated
/// order of magnitude, not a bound. The page reads one criterion at its cap and grades its window with a rich
/// profile (the 2026-09-07 report's shape) through the real grading port; the profile build itself is not timed.
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
    private const int UnseenHitsPerCriterion = 2_000;
    private const int ArchivedHitsPerCriterion = 500;
    private const int TotalAds = 40_000;
    private const int Iterations = 40;
    private const string SeedMarker = "cw-criterion-latency-";

    private static readonly DateTimeOffset T0 = DateTimeOffset.UtcNow.AddDays(-10);

    private static readonly FullCandidateMatchProfile RichProfile =
        new(
            new CandidateMatchProfile(
                Title: string.Empty,
                SsykGroupConceptIds: [.. Enumerable.Range(0, 50).Select(g => $"grp-{g:D2}")],
                PreferredRegionConceptIds: [.. Enumerable.Range(0, 21).Select(r => $"reg-{r:D2}")],
                PreferredEmploymentTypeConceptIds: [.. Enumerable.Range(0, 8).Select(e => $"emp-{e:D2}")],
                PreferredMunicipalityConceptIds: [.. Enumerable.Range(0, 100).Select(m => $"mun-{m:D2}")]),
            []);

    [Fact]
    public async Task The_count_and_the_page_at_the_ceiling_against_Adr0045_class_a()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = Guid.NewGuid();
        var seeding = Stopwatch.StartNew();
        var criteria = await SeedAsync(db, userId, ct);
        output.WriteLine(
            $"Seeded {TotalAds:N0} ads, {criteria.Count} criteria × ({UnseenHitsPerCriterion:N0} unseen + "
            + $"{ArchivedHitsPerCriterion:N0} archived) hits in {seeding.Elapsed.TotalSeconds:F0} s.");

        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(userId);
        var counts = new GetCriterionNewAdCountsQueryHandler(db, user);

        // Non-vacuity: each criterion counts exactly its unseen Active hits, and the page cuts at its cap.
        var answer = await counts.Handle(new GetCriterionNewAdCountsQuery(), ct);
        answer.Items.Count.ShouldBe(CompanyWatchCriterion.MaxPerUser);
        answer.Items.ShouldAllBe(i => i.NewAds == UnseenHitsPerCriterion);

        var profileBuilder = Substitute.For<IMatchProfileBuilder>();
        var expander = scope.ServiceProvider.GetRequiredService<IOccupationSynonymExpander>();
        var grading = new PerUserJobAdSearchQuery(
            db, SearchTextResolvers.For(scope, expander), new JobAdSearchQuery(db, SearchTextResolvers.For(scope, expander)));
        var page = new ListNewCriterionAdsQueryHandler(
            db, user, Substitute.For<IFailedAccessLogger>(), profileBuilder, grading);
        var query = new ListNewCriterionAdsQuery(criteria[0].Value);

        profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>()).Returns(RichProfile);
        var graded = (await page.Handle(query, ct))!;
        graded.Rows.Count.ShouldBe(ListNewCriterionAdsQueryHandler.MaxRows);
        graded.Truncated.ShouldBeTrue();
        graded.MatchingAssessed.ShouldBeTrue();

        output.WriteLine($"ADR 0045 class (a): p95 300 ms. {Iterations} warmed iterations each, nearest-rank p95.");
        var (countP50, countP95) = await MeasureAsync(() => counts.Handle(new GetCriterionNewAdCountsQuery(), ct).AsTask(), ct);
        output.WriteLine($"GET /new-ad-counts, 20 criteria at the ceiling          p50 {countP50,6:F1} ms  p95 {countP95,6:F1} ms");

        var (pageP50, pageP95) = await MeasureAsync(() => page.Handle(query, ct).AsTask(), ct);
        output.WriteLine($"GET /{{id}}/new-ads, cap {ListNewCriterionAdsQueryHandler.MaxRows}, rich profile graded      p50 {pageP50,6:F1} ms  p95 {pageP95,6:F1} ms");

        profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>()).Returns(
            new FullCandidateMatchProfile(new CandidateMatchProfile("", [], [], [], []), []));
        var (bareP50, bareP95) = await MeasureAsync(() => page.Handle(query, ct).AsTask(), ct);
        output.WriteLine($"GET /{{id}}/new-ads, cap {ListNewCriterionAdsQueryHandler.MaxRows}, no stated occupation     p50 {bareP50,6:F1} ms  p95 {bareP95,6:F1} ms");

        // Reverse order, the 2026-09-07 protocol's control for order effects.
        var (pageAgainP50, pageAgainP95) = await MeasureAsync(() => page.Handle(query, ct).AsTask(), ct);
        var (countAgainP50, countAgainP95) = await MeasureAsync(
            () => counts.Handle(new GetCriterionNewAdCountsQuery(), ct).AsTask(), ct);
        output.WriteLine($"Control pass: page p50 {pageAgainP50:F1} / p95 {pageAgainP95:F1} ms; count p50 {countAgainP50:F1} / p95 {countAgainP95:F1} ms");
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

    /// <summary>
    /// TRUNCATE-and-own, then seed. The criteria and their scan state go through the domain (Create, RecordScan);
    /// the ads and the hits are bulk-inserted in the shapes the ingest and the scan write — an Active ad whose hit is
    /// stamped after the criterion's last change, and Archived ads whose hits the purge has not reached — because
    /// tens of thousands of rows through EF would measure the seed, not the read. Then VACUUM (ANALYZE), so the
    /// planner reads real statistics and no autovacuum lands mid-measurement.
    /// </summary>
    private static async Task<List<CompanyWatchCriterionId>> SeedAsync(
        AppDbContext db, Guid userId, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync(
            "TRUNCATE company_watch_criterion_ad_hits, company_watch_criteria CASCADE; TRUNCATE job_ads CASCADE;", ct);

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
            + "'grp-' || lpad((i % 80)::text, 2, '0'), 'reg-' || lpad((i % 21)::text, 2, '0'), "
            + "'mun-' || lpad((i % 290)::text, 2, '0'), 'emp-' || lpad((i % 8)::text, 2, '0') "
            + $"FROM generate_series(0, {TotalAds - 1}) AS i;";
        await db.Database.ExecuteSqlRawAsync(seedAds, ct);

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

        // Every criterion holds a hit on each of the first 2 500 ads: 2 000 Active, 500 Archived. The stamps are
        // distinct and after the criterion's creation, as the scan's per-hit clock writes them.
        var pattern = SeedMarker + "%";
        var from = SeedMarker.Length + 1;
        var hitsPerCriterion = UnseenHitsPerCriterion + ArchivedHitsPerCriterion;
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO company_watch_criterion_ad_hits
                (id, user_id, job_ad_id, company_watch_criterion_id, created_at,
                 read_in_notification_feed, dismissed_from_notification_feed)
            SELECT gen_random_uuid(), c.user_id, j.id, c.id,
                   now() - interval '1 day' + (row_number() OVER () || ' milliseconds')::interval, false, false
            FROM company_watch_criteria c
            CROSS JOIN (SELECT id FROM job_ads
                        WHERE external_id LIKE {pattern}
                          AND substring(external_id from {from})::int < {hitsPerCriterion}) j
            WHERE c.user_id = {userId}
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
