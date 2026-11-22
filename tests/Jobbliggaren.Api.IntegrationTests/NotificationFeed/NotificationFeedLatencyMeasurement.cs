using System.Data.Common;
using System.Diagnostics;
using Jobbliggaren.Api.IntegrationTests.CompanyWatches;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.IntegrationTests.JobAds;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries.GetTaxonomyTree;
using Jobbliggaren.Application.Matching.Profiles;
using Jobbliggaren.Application.NotificationFeed;
using Jobbliggaren.Application.NotificationFeed.Commands.MarkNotificationRead;
using Jobbliggaren.Application.NotificationFeed.Queries.GetMyNotificationFeed;
using Jobbliggaren.Application.NotificationFeed.Sources;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Matching;
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

namespace Jobbliggaren.Api.IntegrationTests.NotificationFeed;

/// <summary>
/// #1663 PR 3 (ADR 0175 D8) — the bell with its three sources against ADR 0045 class (a), read/list: <b>p95 300 ms</b>,
/// as handler latency, and the cost halves the bell's two buckets are re-derived from: the read
/// (<c>RateLimitingOptions.NotificationFeedRead</c>, security-auditor's rule (b)6 (i)–(v) of 2026-11-20) and the
/// Branschbevakning source's "Markera alla" (<c>RateLimitingOptions.NotificationFeedMarkThrough</c>, her Major 1, measured under
/// dotnet-architect's protocol). senior-cto-advisor's ruling of the same day orders both.
///
/// <para>
/// <b>The fixture, registered before the first run.</b> One user with PR 2's ceiling profile (391 occupation groups, 21
/// regions, 290 municipalities) and the background-match consent, over <see cref="CriterionHitStockSeed"/>: 50 000 Active
/// ads, each holding exactly one of 20 criteria × 2 500 unseen hits, plus 500 hits per criterion on Archived ads. The hits
/// are stamped in ad order, and two layers sit on the newest ads. A match row on each of the newest 5 000; a follow hit, all
/// under one watch on the company those ads carry, on each of the newest 15 000. So the match source holds the newest
/// 5 000 Branschbevakning ads and the follow source the newest 15 000. Of the follow source's own hits, the newest 5 000
/// are held by the match source. The 10 000 it counts are its <c>MaxCountedHits</c>, so its grade path counts at the bound
/// it declares rather than refusing.
/// </para>
///
/// <para>
/// <b>Why the held ads are the newest.</b> Each lower source's window reads newest first and skips what a higher source
/// holds, so this layout makes every window walk past all the held hits before it reaches one it shows. It is the costliest
/// arrangement of the same rows. An account produces it by following the companies its Branschbevakning reaches.
/// </para>
///
/// <para>
/// <b>The read</b> (rule (ii)). p is the highest handler p95 over the base case and the shortfall case. Each case runs with
/// the follow source on its common path (the watch without a filter) and on its grade path ("endast matchade"), then again
/// in reverse order as the control pass. The shortfall case is the base case after clicks, through the click's own handler,
/// on the newest rows each source shows. Every window then holds fewer unread rows than its count, and every source names
/// the newest unread ones with its added statement.
/// </para>
///
/// <para>
/// <b>M0 and M1</b> (senior-cto-advisor's measurement ruling of 2026-11-20, registered before either ran). The fixture above is
/// M0, and its base case carries a control pass: the Branschbevakning type switched off, so the bell is as it was before PR 3.
/// The control is reported beside p and never counted in it. M1 is the everyday case: the same layers with one criterion,
/// which is what each user on the box had at S-2's reading.
/// </para>
///
/// <para>
/// <b>"Markera alla"</b>, under the architect's protocol. Autovacuum is off on the hit table, and ANALYZE runs while the
/// stock is unread. The first call runs under EXPLAIN (ANALYZE, BUFFERS) in a transaction that is rolled back, then for
/// real, read against ADR 0045 class (c), 400 ms. Then a timed loop of calls that write nothing, straight away and again
/// after VACUUM (ANALYZE). The rule is ⌊1000 ÷ the higher of the two p95s⌋ ≥ 60, with the plan shape checked before any
/// number.
/// </para>
///
/// <para>
/// ⚠ <b>OBSERVE-ONLY</b> (ADR 0045): durations and plans go to test output and into the dated report. What is asserted is
/// that the measured state is the seeded one: each source's count, the statement count of the base case and the shortfall
/// case, and that the grade path counts rather than refuses.
/// </para>
///
/// <para>
/// <b>Run on request:</b> the command is the Regenerate section of <c>docs/reviews/2026-11-20-1663-pr3-latency.md</c>.
/// </para>
/// </summary>
[Collection("JobAdBrowsePlan")]
[Trait("Category", "SmokeTest")]
public class NotificationFeedLatencyMeasurement(JobAdBrowsePlanFixture fixture, ITestOutputHelper output)
{
    private const int CeilingOccupationGroups = 391;
    private const int MatchedAds = 5_000;
    private const int FollowedAds = 15_000;
    private const int ClickedPerSource = 200;
    private const string FollowedCompany = "5590000017";

    private static readonly DateTimeOffset T0 = DateTimeOffset.UtcNow.AddDays(-10);

    // Seed indices: the newest ads are the highest, and every index from FirstFollowed up is an Active ad.
    private static readonly int EndOfAds = CriterionHitStockSeed.TotalAds;
    private static readonly int FirstMatched = EndOfAds - MatchedAds;
    private static readonly int FirstFollowed = EndOfAds - FollowedAds;

    /// <summary>
    /// M0, the registered fixture and protocol, which give the official figures, with the control pass that
    /// senior-cto-advisor's measurement ruling of 2026-11-20 made part of it: the bell without the Branschbevakning source.
    /// </summary>
    [Fact(Explicit = true)]
    public async Task The_bell_with_three_sources_and_the_criterion_mark_all_against_Adr0045()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = fixture.Services.CreateAsyncScope();
        var run = await SetUpAsync(scope, "M0", CompanyWatchCriterion.MaxPerUser, ct);

        var p95s = new List<(string Pass, double P95)>();
        await MeasureBothCasesAsync(run, p95s, withControl: true, ct);
        ReportReadRule(run.Label, p95s);

        // ── "Markera alla" on the Branschbevakning source ───────────────────────────────────────
        await MeasureMarkThroughAsync(run.Db, run.Bell, run.UserId, ct);
    }

    /// <summary>
    /// M1, the everyday case (senior-cto-advisor, 2026-11-20, registered before it ran): one Branschbevakning, which is
    /// what each user on the box has (S-2, 2026-11-20: 4 users, 4 criteria), holding its share of the shared seed
    /// (2 500 new hits and 500 on Archived ads), with the match and follow layers unchanged.
    /// </summary>
    [Fact(Explicit = true)]
    public async Task The_bell_with_one_Branschbevakning_the_everyday_case_against_Adr0045()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = fixture.Services.CreateAsyncScope();
        var run = await SetUpAsync(scope, "M1", criteria: 1, ct);

        var p95s = new List<(string Pass, double P95)>();
        await MeasureBothCasesAsync(run, p95s, withControl: false, ct);
        ReportReadRule(run.Label, p95s);
    }

    private sealed record Run(
        string Label, int Criteria, AppDbContext Db, Guid UserId, ICurrentUser User, GetMyNotificationFeedQueryHandler Bell,
        Func<Task> Read);

    private async Task<Run> SetUpAsync(AsyncServiceScope scope, string label, int criteria, CancellationToken ct)
    {
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

        await CriterionHitStockSeed.SeedAsync(db, userId, tree, groups, T0, criteria, ct);
        await SeedBellLayersAsync(userId, tree, groups, ct);
        output.WriteLine(
            $"{label}: seeded {CriterionHitStockSeed.TotalAds:N0} ads, {criteria} criteria × "
            + $"({CriterionHitStockSeed.UnseenHitsPerCriterion:N0} unseen + {CriterionHitStockSeed.ArchivedHitsPerCriterion:N0} "
            + $"archived) hits, {MatchedAds:N0} match rows and {FollowedAds:N0} follow hits on the newest ads, in "
            + $"{seeding.Elapsed.TotalSeconds:F0} s.");

        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(userId);
        var profileBuilder = new MatchProfileBuilder(db, user, taxonomy);
        var expander = scope.ServiceProvider.GetRequiredService<IOccupationSynonymExpander>();
        var grading = new PerUserJobAdSearchQuery(
            db, SearchTextResolvers.For(scope, expander), new JobAdSearchQuery(db, SearchTextResolvers.For(scope, expander)));
        var bell = new GetMyNotificationFeedQueryHandler(db, user, profileBuilder, grading);

        var profile = (await profileBuilder.BuildFullForSortAsync(ct)).Fast;
        profile.SsykGroupConceptIds.Count.ShouldBe(CeilingOccupationGroups);
        profile.PreferredRegionConceptIds.Count.ShouldBe(tree.Regions.Count);
        profile.PreferredMunicipalityConceptIds.Count.ShouldBe(tree.Regions.Sum(r => r.Municipalities.Count));
        output.WriteLine(
            $"Profile: {profile.SsykGroupConceptIds.Count} occupation groups, {profile.PreferredRegionConceptIds.Count} "
            + $"regions, {profile.PreferredMunicipalityConceptIds.Count} municipalities.");

        return new Run(label, criteria, db, userId, user, bell, () => bell.Handle(new GetMyNotificationFeedQuery(), ct).AsTask());
    }

    /// <summary>The base case, then (for M0) the control pass, then the shortfall case after the clicks.</summary>
    private async Task MeasureBothCasesAsync(
        Run run, List<(string Pass, double P95)> p95s, bool withControl, CancellationToken ct)
    {
        // Criterion k holds every 20th Active ad, and the follow source holds the newest 15 000 of them, so each criterion
        // has 750 held hits and 1 750 the source counts.
        var heldPerCriterion = FollowedAds / (CriterionHitStockSeed.ActiveAds / CriterionHitStockSeed.UnseenHitsPerCriterion);
        var unheldCriterionAds = run.Criteria * (CriterionHitStockSeed.UnseenHitsPerCriterion - heldPerCriterion);
        var countedFollowAds = FollowedAds - MatchedAds;
        await AssertStateAsync(
            run.Db, run.Bell, $"{run.Label} base", statements: 13, matches: MatchedAds, follows: countedFollowAds,
            criteria: unheldCriterionAds, ct);
        countedFollowAds.ShouldBe(FollowedCompanyAdFeedSource.MaxCountedHits);

        output.WriteLine(
            $"ADR 0045 class (a): p95 300 ms. {LatencyMeasurementTools.Iterations} warmed iterations each, nearest-rank p95.");
        await MeasurePassesAsync(run.Db, run.UserId, run.Bell, run.Read, $"{run.Label} base", countedFollowAds, p95s, ct);

        if (withControl)
            await MeasureWithoutTheCriterionSourceAsync(run, countedFollowAds, ct);

        // ── The shortfall case: clicks on the newest rows each source shows ────────────────────
        var clicked = await ClickNewestShownRowsAsync(run.Db, run.User, run.UserId, ct);
        output.WriteLine($"{run.Label} shortfall case: {clicked} ads clicked through MarkNotificationReadCommandHandler.");
        await AssertStateAsync(
            run.Db, run.Bell, $"{run.Label} shortfall", statements: 16, matches: MatchedAds - ClickedPerSource,
            follows: countedFollowAds - ClickedPerSource, criteria: unheldCriterionAds - ClickedPerSource, ct);
        await MeasurePassesAsync(
            run.Db, run.UserId, run.Bell, run.Read, $"{run.Label} shortfall", countedFollowAds - ClickedPerSource, p95s, ct);
    }

    /// <summary>
    /// The control (senior-cto-advisor, 2026-11-20): the bell as it was before PR 3. The Branschbevakning type is switched
    /// off through the domain on the same fixture, so the source stops at its first statement; its passes are reported
    /// beside the read bucket's and never count in p. The type is switched back on before the shortfall case.
    /// </summary>
    private async Task MeasureWithoutTheCriterionSourceAsync(Run run, int countedFollowAds, CancellationToken ct)
    {
        await SetCriterionTypeAsync(run.Db, run.UserId, on: false, ct);
        using (var capture = new CommandCapture(run.Db))
        {
            var off = await run.Bell.Handle(new GetMyNotificationFeedQuery(), ct);
            capture.Commands.Count.ShouldBe(10, "match 4, follow 5, and the Branschbevakning source's first statement");
            Count(off, NotificationFeedSourceKind.CompanyWatchCriterionAd).ShouldBe(0);
            Count(off, NotificationFeedSourceKind.FollowedCompanyAd).ShouldBe(countedFollowAds);
        }

        await MeasurePassesAsync(
            run.Db, run.UserId, run.Bell, run.Read, $"{run.Label} control, criterion off, base", countedFollowAds, [], ct);
        await SetCriterionTypeAsync(run.Db, run.UserId, on: true, ct);
    }

    private static async Task SetCriterionTypeAsync(AppDbContext db, Guid userId, bool on, CancellationToken ct)
    {
        var seeker = await db.JobSeekers.SingleAsync(js => js.UserId == userId, ct);
        seeker.SetNotificationFeedCriterionAds(on, new CriterionHitStockSeed.FixedClock(DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    /// <summary>
    /// Rule (iii) over every pass (security-auditor's rule (ii)) and over the primary passes (senior-cto-advisor's item 2,
    /// which uses those only if M3 shows the slow tail is the machine's).
    /// </summary>
    private void ReportReadRule(string label, List<(string Pass, double P95)> p95s)
    {
        var primary = p95s.Where(x => !x.Pass.Contains("(control)", StringComparison.Ordinal)).ToList();
        foreach (var (name, passes) in new[] { ("every pass", p95s), ("primary passes", primary) })
        {
            var p = passes.Max(x => x.P95);
            var perMinute = (int)Math.Floor(1000 / p);
            output.WriteLine(
                $"{label} read bucket, rule (iii), {name}: p = {p:F1} ms ({passes.MaxBy(x => x.P95).Pass}); "
                + $"⌊1000 ÷ p⌋ = {perMinute} ({(perMinute >= 12 ? "≥" : "<")} 12); 12 × p = {12 * p / 1000:F2} s/min.");
        }
    }

    /// <summary>
    /// The seeker, the watch and the two layers on the newest ads, through the aggregates in a context of their own, so the
    /// measuring context tracks none of them. The followed ads carry the watched company's org number, as the follow scan
    /// requires of a hit; no read in the bell compares the two. Stamps follow ad order, as the shared seed's do.
    /// </summary>
    private async Task SeedBellLayersAsync(Guid userId, TaxonomyTreeDto tree, string[] groups, CancellationToken ct)
    {
        await using var seedScope = fixture.Services.CreateAsyncScope();
        var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
        var clock = new CriterionHitStockSeed.FixedClock(T0);

        var seeker = JobSeeker.Register(userId, TermsAcceptance.AcceptCurrent(clock), clock).Value;
        seeker.UpdateMatchPreferences(
            MatchPreferences.Create(
                groups,
                tree.Regions.Select(r => r.ConceptId),
                null,
                tree.Regions.SelectMany(r => r.Municipalities).Select(m => m.ConceptId)).Value,
            clock);
        seeker.UpdateNotificationConsent(true, clock);
        db.JobSeekers.Add(seeker);

        var watch = CompanyWatch.Follow(userId, OrganizationNumber.Create(FollowedCompany).Value, clock).Value;
        db.CompanyWatches.Add(watch);

        var ads = await ActiveAdsFromAsync(db, FirstFollowed, ct);
        ads.Count.ShouldBe(FollowedAds);
        var stamps = DateTimeOffset.UtcNow.AddDays(-1).AddHours(-2);
        foreach (var (index, adId) in ads)
        {
            if (index >= FirstMatched)
            {
                db.UserJobAdMatches.Add(UserJobAdMatch.Create(
                    userId, adId, NotifiableMatchGrade.Strong, ["csharp"],
                    new CriterionHitStockSeed.FixedClock(stamps.AddMilliseconds(index))).Value);
            }

            db.FollowedCompanyAdHits.Add(FollowedCompanyAdHit.Create(
                userId, adId, watch.Id, new CriterionHitStockSeed.FixedClock(stamps.AddHours(1).AddMilliseconds(index))).Value);
        }

        await db.SaveChangesAsync(ct);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE job_ads SET organization_number = {FollowedCompany}
            WHERE external_id LIKE {CriterionHitStockSeed.SeedMarker + "%"}
              AND substring(external_id from {CriterionHitStockSeed.SeedMarker.Length + 1})::int >= {FirstFollowed}
            """,
            ct);

        // One statement per command: VACUUM refuses an implicit transaction.
        await db.Database.ExecuteSqlRawAsync("VACUUM (ANALYZE) job_ads;", ct);
        await db.Database.ExecuteSqlRawAsync("VACUUM (ANALYZE) user_job_ad_matches;", ct);
        await db.Database.ExecuteSqlRawAsync("VACUUM (ANALYZE) followed_company_ad_hits;", ct);
        await db.Database.ExecuteSqlRawAsync("VACUUM (ANALYZE) company_watches;", ct);
        await db.Database.ExecuteSqlRawAsync("VACUUM (ANALYZE) job_seekers;", ct);
    }

    /// <summary>The Active seeded ads from <paramref name="firstIndex"/> on, by their index in the seed.</summary>
    private static async Task<List<(int Index, JobAdId Id)>> ActiveAdsFromAsync(
        AppDbContext db, int firstIndex, CancellationToken ct)
    {
        var from = CriterionHitStockSeed.SeedMarker.Length + 1;
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var command = new NpgsqlCommand(
                $"""
                SELECT id, substring(external_id from {from})::int AS index FROM job_ads
                WHERE external_id LIKE @pattern AND status = 'Active'
                  AND substring(external_id from {from})::int >= @first
                ORDER BY index
                """,
                (NpgsqlConnection)db.Database.GetDbConnection());
            command.Parameters.AddWithValue("pattern", CriterionHitStockSeed.SeedMarker + "%");
            command.Parameters.AddWithValue("first", firstIndex);
            var ads = new List<(int, JobAdId)>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                ads.Add((reader.GetInt32(1), new JobAdId(reader.GetGuid(0))));
            return ads;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// The newest <see cref="ClickedPerSource"/> rows each source shows, clicked through the click's own handler and
    /// committed as the unit of work commits it: the match source's newest ads, the follow source's newest ads no match row
    /// holds, and the Branschbevakning source's newest Active ads neither a match row nor a follow hit holds.
    /// </summary>
    private static async Task<int> ClickNewestShownRowsAsync(
        AppDbContext db, ICurrentUser user, Guid userId, CancellationToken ct)
    {
        const string NoMatch =
            "NOT EXISTS (SELECT 1 FROM user_job_ad_matches m WHERE m.user_id = @user AND m.job_ad_id = x.job_ad_id)";
        const string NoFollow =
            "NOT EXISTS (SELECT 1 FROM followed_company_ad_hits f WHERE f.user_id = @user AND f.job_ad_id = x.job_ad_id)";
        var clicks = new List<Guid>();
        clicks.AddRange(await NewestAsync(
            db, "SELECT x.job_ad_id FROM user_job_ad_matches x WHERE x.user_id = @user ORDER BY x.created_at DESC LIMIT @n",
            userId, ct));
        clicks.AddRange(await NewestAsync(
            db, $"SELECT x.job_ad_id FROM followed_company_ad_hits x WHERE x.user_id = @user AND {NoMatch} "
                + "ORDER BY x.created_at DESC LIMIT @n",
            userId, ct));
        clicks.AddRange(await NewestAsync(
            db, "SELECT x.job_ad_id FROM company_watch_criterion_ad_hits x JOIN job_ads j ON j.id = x.job_ad_id "
                + $"WHERE x.user_id = @user AND j.status = 'Active' AND {NoMatch} AND {NoFollow} "
                + "ORDER BY x.created_at DESC LIMIT @n",
            userId, ct));
        clicks.Distinct().Count().ShouldBe(3 * ClickedPerSource);

        var click = new MarkNotificationReadCommandHandler(db, user);
        foreach (var adId in clicks)
        {
            (await click.Handle(new MarkNotificationReadCommand(adId), ct)).IsSuccess.ShouldBeTrue();
            await db.SaveChangesAsync(ct);
        }

        db.ChangeTracker.Clear();
        await db.Database.ExecuteSqlRawAsync("VACUUM (ANALYZE) user_job_ad_matches;", ct);
        await db.Database.ExecuteSqlRawAsync("VACUUM (ANALYZE) followed_company_ad_hits;", ct);
        await db.Database.ExecuteSqlRawAsync("VACUUM (ANALYZE) company_watch_criterion_ad_hits;", ct);
        return clicks.Count;
    }

    private static async Task<List<Guid>> NewestAsync(AppDbContext db, string sql, Guid userId, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var command = new NpgsqlCommand(sql, (NpgsqlConnection)db.Database.GetDbConnection());
            command.Parameters.AddWithValue("user", userId);
            command.Parameters.AddWithValue("n", ClickedPerSource);
            var ids = new List<Guid>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                ids.Add(reader.GetGuid(0));
            return ids;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// Non-vacuity on the common path: the statement count of the case, and each source's count as the seed and the clicks
    /// fix it. Every statement's plan, EXPLAIN (ANALYZE, BUFFERS), goes to the output.
    /// </summary>
    private async Task AssertStateAsync(
        AppDbContext db, GetMyNotificationFeedQueryHandler bell, string name, int statements, int matches, int follows,
        int criteria, CancellationToken ct)
    {
        NotificationFeedDto feed;
        List<CapturedCommand> sent;
        using (var capture = new CommandCapture(db))
        {
            feed = await bell.Handle(new GetMyNotificationFeedQuery(), ct);
            sent = [.. capture.Commands];
        }

        sent.Count.ShouldBe(statements, $"the {name} case's statements on the common path");
        Count(feed, NotificationFeedSourceKind.Match).ShouldBe(matches);
        Count(feed, NotificationFeedSourceKind.FollowedCompanyAd).ShouldBe(follows);
        Count(feed, NotificationFeedSourceKind.CompanyWatchCriterionAd).ShouldBe(criteria);
        feed.Items.Count.ShouldBe(GetMyNotificationFeedQueryHandler.MaxItems);
        feed.Items.ShouldAllBe(i => i.IsUnread);
        output.WriteLine(
            $"{name}: {sent.Count} statements; unread match {matches:N0}, follow {follows:N0}, Branschbevakning {criteria:N0}.");

        for (var i = 0; i < sent.Count; i++)
        {
            output.WriteLine($"── {name}, statement {i + 1} of {sent.Count}, EXPLAIN (ANALYZE, BUFFERS):");
            output.WriteLine(sent[i].Sql);
            foreach (var line in await LatencyMeasurementTools.ExplainAsync(db, sent[i], ct))
                output.WriteLine(line);
        }
    }

    /// <summary>The common path, the grade path, then the control pass in reverse order.</summary>
    private async Task MeasurePassesAsync(
        AppDbContext db, Guid userId, GetMyNotificationFeedQueryHandler bell, Func<Task> read, string name,
        int countedFollowAds, List<(string Pass, double P95)> p95s, CancellationToken ct)
    {
        async Task PassAsync(string path)
        {
            var (p50, p95) = await LatencyMeasurementTools.MeasureAsync(read, ct);
            var pass = $"{name}, follow {path}";
            p95s.Add((pass, p95));
            output.WriteLine($"GET /notification-feed, {pass,-34} p50 {p50,6:F1} ms  p95 {p95,6:F1} ms");
        }

        await PassAsync("common path");
        await SetOnlyMatchedAsync(db, userId, onlyMatched: true, ct);
        var graded = await bell.Handle(new GetMyNotificationFeedQuery(), ct);
        var gradedCount = Count(graded, NotificationFeedSourceKind.FollowedCompanyAd);
        gradedCount.ShouldNotBeNull("the grade path counts at its bound rather than refusing");
        output.WriteLine($"{name}: the grade path counts {gradedCount:N0} of the {countedFollowAds:N0} unheld followed ads.");
        await PassAsync("grade path");
        await PassAsync("grade path (control)");
        await SetOnlyMatchedAsync(db, userId, onlyMatched: false, ct);
        await PassAsync("common path (control)");
    }

    private static async Task SetOnlyMatchedAsync(AppDbContext db, Guid userId, bool onlyMatched, CancellationToken ct)
    {
        var watch = await db.CompanyWatches.SingleAsync(w => w.UserId == userId, ct);
        if (onlyMatched)
            watch.SetFilter(WatchFilterSpec.Create([], [], onlyMatched: true).Value).IsSuccess.ShouldBeTrue();
        else
            watch.ClearFilter();
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    /// <summary>
    /// dotnet-architect's protocol for the Branschbevakning source's mark-through (security-auditor Major 1). The statement
    /// is captured without being sent, so the only rolled-back write before the real one is the EXPLAIN the protocol names.
    /// </summary>
    private async Task MeasureMarkThroughAsync(
        AppDbContext db, GetMyNotificationFeedQueryHandler bell, Guid userId, CancellationToken ct)
    {
        var source = new CompanyWatchCriterionAdFeedSource();
        var context = new NotificationFeedMarkContext(db, userId);
        var before = await bell.Handle(new GetMyNotificationFeedQuery(), ct);
        var through = before.Sources.Single(s => s.Kind == NotificationFeedSourceKind.CompanyWatchCriterionAd)
            .AcknowledgedThrough.ShouldNotBeNull();

        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE company_watch_criterion_ad_hits SET (autovacuum_enabled = false);", ct);
        try
        {
            await db.Database.ExecuteSqlRawAsync("ANALYZE company_watch_criterion_ad_hits;", ct);
            var unread = await db.CompanyWatchCriterionAdHits.CountAsync(h => h.UserId == userId && !h.ReadInNotificationFeed, ct);
            output.WriteLine($"Mark-through: autovacuum off, ANALYZE with {unread:N0} of the user's hits unread.");

            var statement = await CaptureWithoutSendingAsync(db, userId, through, ct);
            output.WriteLine("Mark-through, first call, EXPLAIN (ANALYZE, BUFFERS) inside BEGIN … ROLLBACK:");
            output.WriteLine(statement.Sql);
            foreach (var line in await LatencyMeasurementTools.ExplainAsync(db, statement, ct))
                output.WriteLine(line);

            var first = Stopwatch.StartNew();
            await source.MarkThroughAsync(context, through, ct);
            first.Stop();
            var marked = unread - await db.CompanyWatchCriterionAdHits.CountAsync(
                h => h.UserId == userId && !h.ReadInNotificationFeed, ct);
            output.WriteLine(
                $"Mark-through, first call for real: {first.Elapsed.TotalMilliseconds:F1} ms, {marked:N0} hits marked "
                + $"(ADR 0045 class (c): 400 ms).");
            var after = await bell.Handle(new GetMyNotificationFeedQuery(), ct);
            Count(after, NotificationFeedSourceKind.CompanyWatchCriterionAd).ShouldBe(0);

            var noOp = await MeasureNoOpAsync(db, source, context, statement, through, "straight away", ct);
            await db.Database.ExecuteSqlRawAsync("VACUUM (ANALYZE) company_watch_criterion_ad_hits;", ct);
            var vacuumed = await MeasureNoOpAsync(db, source, context, statement, through, "after VACUUM (ANALYZE)", ct);

            var worst = Math.Max(noOp, vacuumed);
            var perMinute = (int)Math.Floor(1000 / worst);
            output.WriteLine(
                $"Mark bucket rule: ⌊1000 ÷ {worst:F1}⌋ = {perMinute} ({(perMinute >= 60 ? "≥" : "<")} 60).");
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE company_watch_criterion_ad_hits RESET (autovacuum_enabled);", CancellationToken.None);
        }
    }

    private async Task<double> MeasureNoOpAsync(
        AppDbContext db, CompanyWatchCriterionAdFeedSource source, NotificationFeedMarkContext context,
        CapturedCommand statement, DateTimeOffset through, string when, CancellationToken ct)
    {
        output.WriteLine($"Mark-through, a call that writes nothing, {when}, EXPLAIN (ANALYZE, BUFFERS):");
        foreach (var line in await LatencyMeasurementTools.ExplainAsync(db, statement, ct))
            output.WriteLine(line);
        var (p50, p95) = await LatencyMeasurementTools.MeasureAsync(() => source.MarkThroughAsync(context, through, ct), ct);
        output.WriteLine($"Mark-through, no-op, {when,-24} p50 {p50,6:F1} ms  p95 {p95,6:F1} ms");
        return p95;
    }

    /// <summary>The mark-through's statement as EF sends it, captured on a context of its own that never sends it.</summary>
    private static async Task<CapturedCommand> CaptureWithoutSendingAsync(
        AppDbContext db, Guid userId, DateTimeOffset through, CancellationToken ct)
    {
        var capture = new SuppressingCapture();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(db.Database.GetConnectionString(), npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(warnings => warnings.Log(CoreEventId.ManyServiceProvidersCreatedWarning))
            .AddInterceptors(capture)
            .Options;
        await using var silent = new AppDbContext(options);
        await new CompanyWatchCriterionAdFeedSource().MarkThroughAsync(new NotificationFeedMarkContext(silent, userId), through, ct);
        return capture.Captured.ShouldNotBeNull();
    }

    private static int? Count(NotificationFeedDto feed, NotificationFeedSourceKind kind) =>
        feed.Sources.Single(s => s.Kind == kind).UnreadCount;

    private sealed class SuppressingCapture : DbCommandInterceptor
    {
        public CapturedCommand? Captured { get; private set; }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Captured = new CapturedCommand(
                command.CommandText,
                [.. command.Parameters.Cast<NpgsqlParameter>().Select(p => p.Clone())]);
            return ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(0));
        }
    }
}
