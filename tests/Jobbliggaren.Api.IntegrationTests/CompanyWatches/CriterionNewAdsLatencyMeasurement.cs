using System.Diagnostics;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.IntegrationTests.JobAds;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.CompanyWatches.Queries.GetCriterionNewAdCounts;
using Jobbliggaren.Application.CompanyWatches.Queries.ListNewCriterionAds;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Profiles;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Infrastructure.Taxonomy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
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
/// holding <see cref="CriterionHitStockSeed.UnseenHitsPerCriterion"/> unseen hits on Active ads, plus hits on Archived ads
/// the next night's purge has not yet removed, over a <c>job_ads</c> table of production's order of size with wide rows
/// (<see cref="CriterionHitStockSeed"/>, the seed the bell's harness measures on too). Nothing caps
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
    private const int CeilingOccupationGroups = 391;

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

        var criteria = await CriterionHitStockSeed.SeedAsync(db, userId, tree, groups, T0, ct);

        var clock = new CriterionHitStockSeed.FixedClock(T0);
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
            $"Seeded {CriterionHitStockSeed.TotalAds:N0} ads, {criteria.Count} criteria × "
            + $"({CriterionHitStockSeed.UnseenHitsPerCriterion:N0} unseen + {CriterionHitStockSeed.ArchivedHitsPerCriterion:N0} "
            + $"archived) hits in {seeding.Elapsed.TotalSeconds:F0} s.");

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
        answer.Items.ShouldAllBe(i => i.NewAds == CriterionHitStockSeed.UnseenHitsPerCriterion);
        output.WriteLine("GET /new-ad-counts, EXPLAIN (ANALYZE, BUFFERS):");
        foreach (var line in await LatencyMeasurementTools.ExplainAsync(db, countStatement, ct))
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

        output.WriteLine(
            $"ADR 0045 class (a): p95 300 ms. {LatencyMeasurementTools.Iterations} warmed iterations each, nearest-rank p95.");
        var (countP50, countP95) = await LatencyMeasurementTools.MeasureAsync(() => counts.Handle(new GetCriterionNewAdCountsQuery(), ct).AsTask(), ct);
        output.WriteLine($"GET /new-ad-counts, 20 criteria                         p50 {countP50,6:F1} ms  p95 {countP95,6:F1} ms");

        var (pageP50, pageP95) = await LatencyMeasurementTools.MeasureAsync(() => page.Handle(query, ct).AsTask(), ct);
        output.WriteLine($"GET /{{id}}/new-ads, cap {ListNewCriterionAdsQueryHandler.MaxRows}, ceiling profile graded   p50 {pageP50,6:F1} ms  p95 {pageP95,6:F1} ms");

        // Reverse order, the 2026-09-07 protocol's control for order effects.
        var (pageAgainP50, pageAgainP95) = await LatencyMeasurementTools.MeasureAsync(() => page.Handle(query, ct).AsTask(), ct);
        var (countAgainP50, countAgainP95) = await LatencyMeasurementTools.MeasureAsync(
            () => counts.Handle(new GetCriterionNewAdCountsQuery(), ct).AsTask(), ct);
        output.WriteLine($"Control pass: page p50 {pageAgainP50:F1} / p95 {pageAgainP95:F1} ms; count p50 {countAgainP50:F1} / p95 {countAgainP95:F1} ms");

        seeker.UpdateMatchPreferences(MatchPreferences.Empty, clock);
        await db.SaveChangesAsync(ct);
        var (bareP50, bareP95) = await LatencyMeasurementTools.MeasureAsync(() => page.Handle(query, ct).AsTask(), ct);
        output.WriteLine($"GET /{{id}}/new-ads, cap {ListNewCriterionAdsQueryHandler.MaxRows}, no stated occupation     p50 {bareP50,6:F1} ms  p95 {bareP95,6:F1} ms");
    }
}
