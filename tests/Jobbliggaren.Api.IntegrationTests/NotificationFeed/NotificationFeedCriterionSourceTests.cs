using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Queries;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.NotificationFeed;
using Jobbliggaren.Application.NotificationFeed.Queries.GetMyNotificationFeed;
using Jobbliggaren.Application.NotificationFeed.Sources;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.Matching;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NSubstitute;
using Shouldly;
using static Jobbliggaren.Api.IntegrationTests.NotificationFeed.NotificationFeedTestSeed;

namespace Jobbliggaren.Api.IntegrationTests.NotificationFeed;

/// <summary>
/// #1663 PR 3 (ADR 0175 D8) — the bell's third source, Branschbevakning, against Testcontainers Postgres and the
/// production endpoints: what one request costs, which statements read a higher source's table, F-A (the bell lists the
/// new set and nothing else), F-B ("Markera alla" marks the counted set), the owner scope of the four writers, the
/// residuals R-a to R-c in the bell, the retention purge, the wire and the plan of the per-ad write.
/// <para>
/// <b>Every state is one production writes.</b> Criteria come from <see cref="CompanyWatchCriterion.Create"/> and the
/// scan's <see cref="CompanyWatchCriterion.RecordScan"/>, criterion hits from <see cref="CompanyWatchCriterionAdHit.Create"/>,
/// follow hits from <see cref="FollowedCompanyAdHit.Create"/> and match rows from <see cref="UserJobAdMatch.Create"/>: the
/// calls the criterion scan, the follow scan and background matching make. Consent, switches, marks, unfollowing and the
/// page's acknowledgement go through their endpoints. Every user is fresh and reads the feed at most four times, inside
/// the feed's read bucket.
/// </para>
/// </summary>
[Collection("Api")]
public class NotificationFeedCriterionSourceTests(ApiFactory factory)
{
    private const string Kind = "CompanyWatchCriterionAd";
    private const string Segment = "company-watch-criterion-ads";
    private const string Match = "Match";
    private const string Followed = "FollowedCompanyAd";
    private const string MatchesSegment = "matches";
    private const string FollowedSegment = "followed-company-ads";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ── what one request costs ───────────────────────────────────────────────────────────────

    // The whole bell's base case: match 4, follow 5, Branschbevakning 4 (its gate, token, count, window). The ad the match
    // source holds and the one the follow source holds are NOT EXISTS inside the criterion source's count and window, never
    // a statement of their own, and its token covers them.
    [Fact]
    public async Task The_whole_bell_runs_thirteen_statements_in_the_base_case_with_an_ad_held_by_each_higher_source()
    {
        var (client, userId) = await RegisterUserAsync(factory, "criterion-cost", Ct);
        await GiveConsentAsync(client, true);
        var watch = await SeedWatchAsync(factory, userId, Ct);
        var criterion = await SeedCriterionAsync(factory, userId, Ct);
        var (heldByMatch, heldByFollow, own) = await SeedHeldTrioAsync(userId, watch, criterion, Base.AddMinutes(1));

        using var scope = factory.Services.CreateScope();
        var counter = new StatementCounter();
        await using var db = NewContext(scope, counter);
        var feed = await HandlerAsync(db, userId);

        feed.Items.Select(i => (i.Kind, i.JobAdId)).ShouldBe(
            [
                (NotificationFeedSourceKind.Match, heldByMatch.Value),
                (NotificationFeedSourceKind.FollowedCompanyAd, heldByFollow.Value),
                (NotificationFeedSourceKind.CompanyWatchCriterionAd, own.Value),
            ],
            ignoreOrder: true);
        feed.Sources.Single(s => s.Kind == NotificationFeedSourceKind.CompanyWatchCriterionAd)
            .ShouldBe(new NotificationFeedSourceDto(NotificationFeedSourceKind.CompanyWatchCriterionAd, 1, Base.AddMinutes(3)));
        // Match: consent + watermark, token, count, list. Followed: watermark, watches, token, count, window.
        // Branschbevakning: its switch with both higher gates and the criterion check, token, count, window.
        counter.Count.ShouldBe(13);
    }

    // Sixteen when read rows crowd the unread ones out of the window in all three sources: each runs its one conditional
    // statement. Driven through the sources in the handler's own order with a small cap, which shrinks every window with
    // it, so eight read rows per source are enough. The held ads change no count here either.
    [Fact]
    public async Task The_whole_bell_runs_sixteen_statements_when_read_rows_crowd_unread_ones_out_in_all_three_sources()
    {
        var (client, userId) = await RegisterUserAsync(factory, "criterion-crowd-cost", Ct);
        await GiveConsentAsync(client, true);
        var watch = await SeedWatchAsync(factory, userId, Ct);
        var criterion = await SeedCriterionAsync(factory, userId, Ct);
        var unreadMatch = await SeedAdAsync(factory, "Oläst match", Ct);
        var unreadFollow = await SeedAdAsync(factory, "Oläst följd", Ct);
        await SeedMatchAsync(factory, userId, unreadMatch, Base.AddMinutes(1), Ct);
        await SeedHitAsync(factory, userId, unreadFollow, watch, Base.AddMinutes(1), Ct);
        var (heldByMatch, heldByFollow, own) = await SeedHeldTrioAsync(userId, watch, criterion, Base.AddMinutes(2));
        for (var i = 0; i < 8; i++)
        {
            var match = await SeedAdAsync(factory, $"Läst match {i}", Ct);
            await SeedMatchAsync(factory, userId, match, Base.AddMinutes(10 + i), Ct);
            var followed = await SeedAdAsync(factory, $"Läst följd {i}", Ct);
            await SeedHitAsync(factory, userId, followed, watch, Base.AddMinutes(10 + i), Ct);
            var criterionAd = await SeedAdAsync(factory, $"Läst bransch {i}", Ct);
            await SeedCriterionHitAsync(factory, criterion, criterionAd, Base.AddMinutes(10 + i), Ct);
            // Each read in the bell with a click, as the panel sends it.
            foreach (var (segment, ad) in new[] { (MatchesSegment, match), (FollowedSegment, followed), (Segment, criterionAd) })
                (await ClickAsync(client, segment, ad)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using var scope = factory.Services.CreateScope();
        var counter = new StatementCounter();
        await using var db = NewContext(scope, counter);
        var context = Context(db, userId, maxItems: 2);
        var reads = new List<NotificationSourceRead>();
        foreach (var source in NotificationFeedSources.All)
            reads.Add(await source.ReadAsync(context, Ct));

        reads.Select(r => r.Kind).ShouldBe(
        [
            NotificationFeedSourceKind.Match,
            NotificationFeedSourceKind.FollowedCompanyAd,
            NotificationFeedSourceKind.CompanyWatchCriterionAd,
        ]);
        reads[0].Items.Where(i => i.IsUnread).Select(i => i.JobAdId).ShouldBe([heldByMatch.Value, unreadMatch.Value]);
        reads[1].Items.Where(i => i.IsUnread).Select(i => i.JobAdId).ShouldBe([heldByFollow.Value, unreadFollow.Value]);
        reads[2].Items.Where(i => i.IsUnread).Select(i => i.JobAdId).ShouldBe([own.Value]);
        // Match: consent + watermark, token, count, list, unread beyond the list. Followed: watermark, watches, token, count,
        // window, unread beyond the window. Branschbevakning: its gate, token, count, window, unread beyond the window.
        counter.Count.ShouldBe(16);
    }

    // The first statement's criterion check is an owner EXISTS and nothing more (CTO ruling 4, 2026-11-20): a criterion the
    // scan has never answered Scanned for is still a criterion, so it costs the empty token read. A check that read the
    // outcome as well would make this user cost what a user without one costs, a second writing of NewCriterionAdSet.
    [Theory]
    [InlineData(null)]
    [InlineData(CompanyWatchCriterionScanOutcome.TooBroad)]
    public async Task A_criterion_that_never_answered_Scanned_costs_exactly_one_statement_more_than_none(
        CompanyWatchCriterionScanOutcome? outcome)
    {
        var (client, userId) = await RegisterUserAsync(factory, "criterion-short-circuit", Ct);
        await GiveConsentAsync(client, true);
        await SeedMatchAsync(factory, userId, await SeedAdAsync(factory, "M", Ct), Base.AddMinutes(1), Ct);
        await SeedHitAsync(
            factory, userId, await SeedAdAsync(factory, "F", Ct), await SeedWatchAsync(factory, userId, Ct), Base.AddMinutes(2), Ct);

        var withNone = await StatementsForOneRequestAsync(userId);
        await SeedCriterionAsync(factory, userId, outcome, Ct);
        var withOne = await StatementsForOneRequestAsync(userId);

        withNone.ShouldBe(10);
        withOne.ShouldBe(withNone + 1, "the empty token read; never the same number");
    }

    // ── which statements read a higher source's table (B5, ADR 0175 D8) ──────────────────────

    // The criterion source names user_job_ad_matches only while the match gate holds and followed_company_ad_hits only
    // while the follow gate holds, and then in exactly its count and its window, never in its gate or its token. With its
    // own switch off it sends its first statement and nothing else.
    [Theory]
    [InlineData("gate-on", true, true)]
    [InlineData("consent-off", false, true)]
    [InlineData("matches-off", false, true)]
    [InlineData("follows-off", true, false)]
    [InlineData("criterion-off", false, false)]
    public async Task The_criterion_source_names_a_higher_sources_table_only_while_that_sources_gate_holds(
        string state, bool readsMatches, bool readsFollows)
    {
        var (client, userId) = await RegisterUserAsync(factory, $"criterion-b5-{state}", Ct);
        await GiveConsentAsync(client, true);
        // A second user whose gates all hold: a gate check that lost its correlation would read their gate as this user's.
        var (other, _) = await RegisterUserAsync(factory, $"criterion-b5-{state}-other", Ct);
        await GiveConsentAsync(other, true);
        var watch = await SeedWatchAsync(factory, userId, Ct);
        await SeedHeldTrioAsync(userId, watch, await SeedCriterionAsync(factory, userId, Ct), Base.AddMinutes(1));
        switch (state)
        {
            case "consent-off":
                await GiveConsentAsync(client, false);
                break;
            case "matches-off":
                (await SwitchAsync(client, MatchesSegment, false)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
                break;
            case "follows-off":
                (await SwitchAsync(client, FollowedSegment, false)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
                break;
            case "criterion-off":
                (await SwitchAsync(client, Segment, false)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
                break;
        }

        using var scope = factory.Services.CreateScope();
        var statements = new StatementText();
        await using var db = NewContext(scope, statements);
        await new CompanyWatchCriterionAdFeedSource().ReadAsync(Context(db, userId), Ct);

        var texts = statements.Texts;
        texts.Count.ShouldBe(state == "criterion-off" ? 1 : 4, "gate, token, count, window; or the gate alone");
        AssertReadInTheCountAndTheWindowOnly(texts, "user_job_ad_matches", readsMatches);
        AssertReadInTheCountAndTheWindowOnly(texts, "followed_company_ad_hits", readsFollows);
    }

    // Each higher gate is checked again inside every statement that reads its table (the shape of
    // The_match_gate_is_checked_in_each_statement_that_reads_a_match_row): the gate is lifted through its own endpoint
    // right after the token, and the count and the window already speak for the ad it no longer holds.
    [Theory]
    [InlineData("consent")]
    [InlineData("follows")]
    public async Task Each_higher_gate_is_checked_again_in_every_statement_after_the_token(string gate)
    {
        var (client, userId) = await RegisterUserAsync(factory, $"criterion-gate-race-{gate}", Ct);
        await GiveConsentAsync(client, true);
        var watch = await SeedWatchAsync(factory, userId, Ct);
        var criterion = await SeedCriterionAsync(factory, userId, Ct);
        var (heldByMatch, heldByFollow, own) = await SeedHeldTrioAsync(userId, watch, criterion, Base.AddMinutes(1));
        var released = gate == "consent" ? heldByMatch : heldByFollow;

        using var scope = factory.Services.CreateScope();
        await using (var control = NewContext(scope))
        {
            (await new CompanyWatchCriterionAdFeedSource().ReadAsync(Context(control, userId), Ct))
                .UnreadCount.ShouldBe(1, "both higher gates hold before the race");
        }

        // The gate is the first statement and the token the second.
        var lift = new AfterStatementInterceptor(2, async () =>
        {
            if (gate == "consent")
                await GiveConsentAsync(client, false);
            else
                (await SwitchAsync(client, FollowedSegment, false)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        });
        await using var db = NewContext(scope, lift);
        var read = await new CompanyWatchCriterionAdFeedSource().ReadAsync(Context(db, userId), Ct);

        lift.Fired.ShouldBeTrue();
        read.UnreadCount.ShouldBe(2);
        read.Items.Where(i => i.IsUnread).Select(i => i.JobAdId).ShouldBe([released.Value, own.Value], ignoreOrder: true);
    }

    // ── the token bounds the read, and an ad is one row (ADR 0162 I4 and D2) ──────────────────

    // Token first, then the count, the window and the shortfall bounded by it (I4, as the match and follow sources pin it): a
    // hit committed between the token and the rest is neither counted, listed nor named, so it stays unread for the next
    // poll. Read rows crowd the older unread hit out of a one-row window, so the shortfall statement runs as well.
    [Fact]
    public async Task A_criterion_hit_committed_after_the_token_is_neither_counted_listed_nor_named()
    {
        var (client, userId) = await RegisterUserAsync(factory, "criterion-race-token", Ct);
        var criterion = await SeedCriterionAsync(factory, userId, Ct);
        var unread = await SeedAdAsync(factory, "Oläst", Ct);
        await SeedCriterionHitAsync(factory, criterion, unread, Base.AddMinutes(1), Ct);
        for (var i = 0; i < CompanyWatchCriterionAdFeedSource.HitsPerRow; i++)
        {
            var crowd = await SeedAdAsync(factory, $"Läst {i}", Ct);
            await SeedCriterionHitAsync(factory, criterion, crowd, Base.AddMinutes(10 + i), Ct);
            (await ClickAsync(client, Segment, crowd)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        var late = await SeedAdAsync(factory, "Sen", Ct);

        using var scope = factory.Services.CreateScope();
        // The gate is the first statement and the token the second.
        var interceptor = new AfterStatementInterceptor(
            2, () => SeedCriterionHitAsync(factory, criterion, late, Base.AddMinutes(30), Ct));
        await using var db = NewContext(scope, interceptor);
        var read = await new CompanyWatchCriterionAdFeedSource().ReadAsync(Context(db, userId, maxItems: 1), Ct);

        interceptor.Fired.ShouldBeTrue();
        read.UnreadCount.ShouldBe(1);
        read.AcknowledgedThrough.ShouldBe(Base.AddMinutes(10 + CompanyWatchCriterionAdFeedSource.HitsPerRow - 1));
        read.Items.Select(i => i.JobAdId).ShouldNotContain(late.Value);
        read.Items.Where(i => i.IsUnread).Select(i => i.JobAdId).ShouldBe([unread.Value]);

        await using var fresh = NewContext(scope);
        (await new CompanyWatchCriterionAdFeedSource().ReadAsync(Context(fresh, userId, maxItems: 1), Ct))
            .UnreadCount.ShouldBe(2);
    }

    // On Postgres: the count is of distinct ads, the shortfall's grouping names the ad once at its newest hit, and the click
    // and the X each reach both hits. Read rows crowd the ad out of a one-row window, so the shortfall statement runs.
    [Fact]
    public async Task An_ad_two_criteria_reach_is_counted_once_named_once_and_marked_through_both_hits()
    {
        var (client, userId) = await RegisterUserAsync(factory, "criterion-two-reach", Ct);
        var first = await SeedCriterionAsync(factory, userId, Ct);
        var second = await SeedCriterionAsync(factory, userId, Ct);
        var shared = await SeedAdAsync(factory, "Två bevakningar", Ct);
        await SeedCriterionHitAsync(factory, first, shared, Base.AddMinutes(1), Ct);
        await SeedCriterionHitAsync(factory, second, shared, Base.AddMinutes(2), Ct);
        for (var i = 0; i < CompanyWatchCriterionAdFeedSource.HitsPerRow; i++)
        {
            var crowd = await SeedAdAsync(factory, $"Läst {i}", Ct);
            await SeedCriterionHitAsync(factory, first, crowd, Base.AddMinutes(10 + i), Ct);
            (await ClickAsync(client, Segment, crowd)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using (var scope = factory.Services.CreateScope())
        await using (var db = NewContext(scope))
        {
            var read = await new CompanyWatchCriterionAdFeedSource().ReadAsync(Context(db, userId, maxItems: 1), Ct);
            read.UnreadCount.ShouldBe(1);
            read.Items.Count(i => i.JobAdId == shared.Value).ShouldBe(1, "one row per ad");
            var named = read.Items.Where(i => i.IsUnread).ShouldHaveSingleItem();
            named.JobAdId.ShouldBe(shared.Value);
            named.OccurredAt.ShouldBe(Base.AddMinutes(2), "the ad's moment is its newest hit");
        }

        (await ClickAsync(client, Segment, shared)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await CriterionHitRowsAsync(factory, userId, shared, Ct)).Select(h => h.ReadInNotificationFeed)
            .ShouldBe([true, true]);
        (await DismissAsync(client, Segment, shared)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await CriterionHitRowsAsync(factory, userId, shared, Ct)).Select(h => h.DismissedFromNotificationFeed)
            .ShouldBe([true, true]);
    }

    // ── F-A: the bell lists the new set and nothing else (CTO ruling 1) ──────────────────────

    // Both halves (architect point 2): a hit acknowledged on its criterion's page of new ads leaves the bell. Read in the
    // bell first, it leaves with the unread count unchanged; unread, it leaves with the count one lower, as a visit to a
    // list reads what it showed. The acknowledgement is the page's own: the window it cut, its token posted back as the
    // client does. It writes the criterion's watermark and no bell flag.
    [Fact]
    public async Task A_hit_acknowledged_on_its_criterions_page_leaves_the_bell_whether_read_in_the_bell_or_not()
    {
        var (client, userId) = await RegisterUserAsync(factory, "criterion-fa", Ct);
        var readFirst = await SeedCriterionAsync(factory, userId, Ct);
        var unreadAtVisit = await SeedCriterionAsync(factory, userId, Ct);
        var notVisited = await SeedCriterionAsync(factory, userId, Ct);
        var read = await SeedAdAsync(factory, "Läst i klockan", Ct);
        var unread = await SeedAdAsync(factory, "Oläst vid besöket", Ct);
        var stays = await SeedAdAsync(factory, "Kvar", Ct);
        await SeedCriterionHitAsync(factory, readFirst, read, Base.AddMinutes(1), Ct);
        await SeedCriterionHitAsync(factory, unreadAtVisit, unread, Base.AddMinutes(2), Ct);
        await SeedCriterionHitAsync(factory, notVisited, stays, Base.AddMinutes(3), Ct);
        Source(await GetFeedAsync(client, Ct), Kind).UnreadCount.ShouldBe(3);

        (await ClickAsync(client, Segment, read)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var clicked = await GetFeedAsync(client, Ct);
        Source(clicked, Kind).UnreadCount.ShouldBe(2);
        clicked.Items.Single(i => i.JobAdId == read.Value).IsUnread.ShouldBeFalse("a read row is still listed");

        await AcknowledgePageAsync(client, readFirst);

        var afterRead = await GetFeedAsync(client, Ct);
        Source(afterRead, Kind).UnreadCount.ShouldBe(2, "the row was already read in the bell");
        afterRead.Items.ShouldNotContain(i => i.JobAdId == read.Value);

        await AcknowledgePageAsync(client, unreadAtVisit);

        var afterUnread = await GetFeedAsync(client, Ct);
        Source(afterUnread, Kind).UnreadCount.ShouldBe(1, "the visit read the row it showed");
        afterUnread.Items.ShouldNotContain(i => i.JobAdId == unread.Value);
        afterUnread.Items.ShouldHaveSingleItem().JobAdId.ShouldBe(stays.Value);
        (await CriterionHitRowsAsync(factory, userId, unread, Ct)).ShouldHaveSingleItem().ReadInNotificationFeed.ShouldBeFalse();
    }

    // ── F-B: "Markera alla" marks the counted set (CTO ruling 2) ─────────────────────────────

    // The oracle in both directions (a wrong alias in a self-correlated EXISTS, efcore #31078's class, marks too much and
    // throws nothing): with the token the feed returned, "Markera alla" marks every unread new hit up to it, the ones a
    // higher source holds included, and leaves unread (a) the hit of a criterion the scan answered NotMaterialised for in
    // between, which is new and unread again when the scan answers Scanned, (b) a hit stamped after the token, and (c)
    // another user's hit on the same ad. The match row and the follow hit of the held ads are not written.
    [Fact]
    public async Task Mark_all_marks_every_new_hit_up_to_the_token_held_ones_included_and_nothing_outside_that_set()
    {
        var (client, userId) = await RegisterUserAsync(factory, "criterion-fb", Ct);
        var (_, otherId) = await RegisterUserAsync(factory, "criterion-fb-other", Ct);
        await GiveConsentAsync(client, true);
        var watch = await SeedWatchAsync(factory, userId, Ct);
        var criterion = await SeedCriterionAsync(factory, userId, Ct);
        var goesStale = await SeedCriterionAsync(factory, userId, Ct);
        var (heldByMatch, heldByFollow, own) = await SeedHeldTrioAsync(userId, watch, criterion, Base.AddMinutes(1));
        var ofStale = await SeedAdAsync(factory, "Bevakningen läses om", Ct);
        await SeedCriterionHitAsync(factory, goesStale, ofStale, Base.AddMinutes(4), Ct);
        await SeedCriterionHitAsync(factory, await SeedCriterionAsync(factory, otherId, Ct), own, Base.AddMinutes(1), Ct);

        var token = Source(await GetFeedAsync(client, Ct), Kind).AcknowledgedThrough.ShouldNotBeNull();
        token.ShouldBe(Base.AddMinutes(4), "the newest new hit, held ones included");
        // (a) The scan's read-age guard answers NotMaterialised for the second criterion before the press.
        await RecordScanAsync(factory, goesStale, CompanyWatchCriterionScanOutcome.NotMaterialised, DateTimeOffset.UtcNow, Ct);
        // (b) The scan stamps a hit after the token.
        var late = await SeedAdAsync(factory, "Efter token", Ct);
        await SeedCriterionHitAsync(factory, criterion, late, token.AddMinutes(1), Ct);

        (await MarkAllAsync(client, Segment, token)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        foreach (var ad in new[] { own, heldByMatch, heldByFollow })
            (await CriterionHitRowsAsync(factory, userId, ad, Ct)).ShouldHaveSingleItem().ReadInNotificationFeed.ShouldBeTrue();
        (await CriterionHitRowsAsync(factory, userId, ofStale, Ct)).ShouldHaveSingleItem().ReadInNotificationFeed
            .ShouldBeFalse("its criterion is not Scanned, so no header counted it");
        (await CriterionHitRowsAsync(factory, userId, late, Ct)).ShouldHaveSingleItem().ReadInNotificationFeed
            .ShouldBeFalse("stamped after the token");
        (await CriterionHitRowsAsync(factory, otherId, own, Ct)).ShouldHaveSingleItem().ReadInNotificationFeed
            .ShouldBeFalse("another user's hit on the same ad");
        (await MatchRowAsync(factory, userId, heldByMatch, Ct)).ReadInNotificationFeed.ShouldBeFalse();
        (await HitRowsAsync(factory, userId, heldByFollow, Ct)).ShouldHaveSingleItem().ReadInNotificationFeed.ShouldBeFalse();

        // The next night the scan answers Scanned again with no edit between: the hit is new once more, and unread.
        await RecordScanAsync(factory, goesStale, CompanyWatchCriterionScanOutcome.Scanned, DateTimeOffset.UtcNow, Ct);

        var feed = await GetFeedAsync(client, Ct);
        Source(feed, Kind).UnreadCount.ShouldBe(2, "the stale criterion's hit and the hit after the token");
        feed.Items.Single(i => i.JobAdId == ofStale.Value).IsUnread.ShouldBeTrue();
        feed.Items.Single(i => i.JobAdId == late.Value).IsUnread.ShouldBeTrue();
        feed.Items.Single(i => i.JobAdId == own.Value).IsUnread.ShouldBeFalse();
    }

    // ── the four writers are the owner's alone (security (b)4) ───────────────────────────────

    // Two users with hits on the same ads. One user's click, X, "Markera alla" and switch-on backlog each reach that user's
    // hit and not one tuple of the other's, every request answers 204, and the same four requests again write no tuple at
    // all: each statement filters on the state it writes. A tuple's xmin is the transaction that wrote its version, and an
    // UPDATE writes a new version even when it sets the value the row already holds, so an unchanged xmin is a tuple no
    // statement wrote.
    [Fact]
    public async Task Each_writer_touches_only_its_own_users_hits_answers_204_and_writes_nothing_when_repeated()
    {
        var (me, myId) = await RegisterUserAsync(factory, "criterion-writers-me", Ct);
        var (_, otherId) = await RegisterUserAsync(factory, "criterion-writers-other", Ct);
        var mine = await SeedCriterionAsync(factory, myId, Ct);
        var theirs = await SeedCriterionAsync(factory, otherId, Ct);
        var shared = new List<JobAdId>();
        for (var i = 0; i < 4; i++)
        {
            var ad = await SeedAdAsync(factory, $"Delad {i}", Ct);
            shared.Add(ad);
            await SeedCriterionHitAsync(factory, mine, ad, Base.AddMinutes(1 + i), Ct);
            await SeedCriterionHitAsync(factory, theirs, ad, Base.AddMinutes(1 + i), Ct);
        }

        var (clicked, dismissed) = (shared[0], shared[1]);
        var mineAtStart = await TupleVersionsAsync(myId);
        var theirsAtStart = await TupleVersionsAsync(otherId);
        var token = Source(await GetFeedAsync(me, Ct), Kind).AcknowledgedThrough.ShouldNotBeNull();

        (await ClickAsync(me, Segment, clicked)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await DismissAsync(me, Segment, dismissed)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await MarkAllAsync(me, Segment, token)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SwitchAsync(me, Segment, false)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        // The scan stamps a hit for each of us after my switch-off: mine is the backlog, theirs sits beside it.
        var whileOff = await SeedAdAsync(factory, "Medan den var av", Ct);
        var afterTheStamp = DateTimeOffset.UtcNow.AddMinutes(10);
        await SeedCriterionHitAsync(factory, mine, whileOff, afterTheStamp, Ct);
        await SeedCriterionHitAsync(factory, theirs, whileOff, afterTheStamp, Ct);
        var theirsBeforeTheBacklog = await TupleVersionsAsync(otherId);
        (await SwitchAsync(me, Segment, true)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await CriterionHitRowsAsync(factory, myId, clicked, Ct)).ShouldHaveSingleItem().ReadInNotificationFeed.ShouldBeTrue();
        var x = (await CriterionHitRowsAsync(factory, myId, dismissed, Ct)).ShouldHaveSingleItem();
        (x.DismissedFromNotificationFeed, x.ReadInNotificationFeed).ShouldBe((true, true));
        foreach (var ad in shared.Skip(2).Append(whileOff))
            (await CriterionHitRowsAsync(factory, myId, ad, Ct)).ShouldHaveSingleItem().ReadInNotificationFeed.ShouldBeTrue();
        theirsAtStart.ShouldBeSubsetOf(theirsBeforeTheBacklog, "the click, the X and Markera alla wrote none of their tuples");
        (await TupleVersionsAsync(otherId)).ShouldBe(theirsBeforeTheBacklog, "the backlog wrote none of their tuples");
        foreach (var ad in shared.Append(whileOff))
        {
            (await CriterionHitRowsAsync(factory, otherId, ad, Ct)).ShouldHaveSingleItem()
                .ShouldSatisfyAllConditions(
                    h => h.ReadInNotificationFeed.ShouldBeFalse(),
                    h => h.DismissedFromNotificationFeed.ShouldBeFalse());
        }

        var mineAfterFirst = await TupleVersionsAsync(myId);
        // The instrument's positive control: each of my four tuples was written once, so none keeps its version.
        mineAfterFirst.Intersect(mineAtStart).ShouldBeEmpty("a written tuple gets a new xmin");

        (await ClickAsync(me, Segment, clicked)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await DismissAsync(me, Segment, dismissed)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await MarkAllAsync(me, Segment, token)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        // Off again first, so the switch-on's backlog statement runs over the hit it already read.
        (await SwitchAsync(me, Segment, false)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SwitchAsync(me, Segment, true)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await TupleVersionsAsync(myId)).ShouldBe(mineAfterFirst, "a repeated request writes no tuple");
        (await TupleVersionsAsync(otherId)).ShouldBe(theirsBeforeTheBacklog);
    }

    // ── R-a, R-b, R-c: the bell's half of ADR 0175's residuals ───────────────────────────────

    // R-a: on a first follow the follow scan cold-starts seven days back and bears a follow hit for an ad already read as a
    // criterion row; the data half is CompanyWatchCriterionScanJobIntegrationTests.RA_AFirstFollowAfterACriterionHit_BearsAFollowHitForTheSameAd.
    // The follow source now holds the ad, and its hit was born unread: the ad rings again, as a followed company's ad.
    [Fact]
    public async Task RA_An_ad_read_as_a_criterion_row_returns_as_an_unread_follow_notification_when_a_follow_hit_is_born_later()
    {
        var (client, userId) = await RegisterUserAsync(factory, "criterion-ra", Ct);
        var ad = await SeedAdAsync(factory, "J", Ct);
        await SeedCriterionHitAsync(factory, await SeedCriterionAsync(factory, userId, Ct), ad, Base.AddMinutes(1), Ct);
        (await GetFeedAsync(client, Ct)).Items.ShouldHaveSingleItem().Kind.ShouldBe(Kind);
        (await ClickAsync(client, Segment, ad)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await SeedHitAsync(factory, userId, ad, await SeedWatchAsync(factory, userId, Ct), WholeSecondNow(), Ct);

        var feed = await GetFeedAsync(client, Ct);
        var row = feed.Items.ShouldHaveSingleItem();
        (row.Kind, row.JobAdId, row.IsUnread).ShouldBe((Followed, ad.Value, true));
        Source(feed, Followed).UnreadCount.ShouldBe(1);
        Source(feed, Kind).UnreadCount.ShouldBe(0);
        (await CriterionHitRowsAsync(factory, userId, ad, Ct)).ShouldHaveSingleItem().ReadInNotificationFeed.ShouldBeTrue();
    }

    // R-b (ADR 0173 R1, inherited): a consent grant lets background matching reach seven days back, and a match row is
    // born for an ad already read as a criterion row; the data half is RB_AMatchBornAfterACriterionHit_IsWrittenForTheSameAd.
    // The match row was born unread and holds the ad: it rings again, as a match.
    [Fact]
    public async Task RB_An_ad_read_as_a_criterion_row_returns_as_an_unread_match_when_a_match_row_is_born_later()
    {
        var (client, userId) = await RegisterUserAsync(factory, "criterion-rb", Ct);
        var ad = await SeedAdAsync(factory, "J", Ct);
        await SeedCriterionHitAsync(factory, await SeedCriterionAsync(factory, userId, Ct), ad, Base.AddMinutes(1), Ct);
        (await GetFeedAsync(client, Ct)).Items.ShouldHaveSingleItem().Kind.ShouldBe(Kind);
        (await ClickAsync(client, Segment, ad)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await GiveConsentAsync(client, true);
        await SeedMatchAsync(factory, userId, ad, WholeSecondNow(), Ct);

        var feed = await GetFeedAsync(client, Ct);
        var row = feed.Items.ShouldHaveSingleItem();
        (row.Kind, row.JobAdId, row.IsUnread).ShouldBe((Match, ad.Value, true));
        Source(feed, Match).UnreadCount.ShouldBe(1);
        Source(feed, Kind).UnreadCount.ShouldBe(0);
    }

    // R-c (ADR 0173 R2's class; the data half is RC_AfterAnUnfollow_TheCriterionHitForTheSameAdStays): while a followed
    // company's ad is held by the follow source, a click on its follow row reaches its criterion hit too, because a click
    // reaches every source's rows of the ad (#2158). Once nothing holds the ad, the criterion row speaks by its own read
    // state: read where the click reached it, unread where nothing did.
    [Theory]
    [InlineData("unfollow")]
    [InlineData("follows-off")]
    public async Task RC_A_held_ad_that_stops_being_held_speaks_by_its_criterion_rows_own_read_state(string release)
    {
        var (client, userId) = await RegisterUserAsync(factory, $"criterion-rc-{release}", Ct);
        var watch = await SeedWatchAsync(factory, userId, Ct);
        var criterion = await SeedCriterionAsync(factory, userId, Ct);
        var clicked = await SeedAdAsync(factory, "Klickad medan den hölls", Ct);
        var untouched = await SeedAdAsync(factory, "Orörd", Ct);
        foreach (var (ad, n) in new[] { (clicked, 1), (untouched, 2) })
        {
            await SeedHitAsync(factory, userId, ad, watch, Base.AddMinutes(n), Ct);
            await SeedCriterionHitAsync(factory, criterion, ad, Base.AddMinutes(10 + n), Ct);
        }

        var held = await GetFeedAsync(client, Ct);
        held.Items.Select(i => i.Kind).ShouldBe([Followed, Followed]);
        Source(held, Kind).UnreadCount.ShouldBe(0, "both ads are the follow source's");
        (await ClickAsync(client, FollowedSegment, clicked)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        if (release == "unfollow")
            (await client.DeleteAsync($"/api/v1/me/company-watches/{watch.Value}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        else
            (await SwitchAsync(client, FollowedSegment, false)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var released = await GetFeedAsync(client, Ct);
        released.Items.Select(i => (i.Kind, i.JobAdId, i.IsUnread))
            .ShouldBe([(Kind, untouched.Value, true), (Kind, clicked.Value, false)]);
        Source(released, Kind).UnreadCount.ShouldBe(1);
        Source(released, Followed).UnreadCount.ShouldBe(0);
    }

    // ── the retention purge (ADR 0175 D10) ───────────────────────────────────────────────────

    // An ad that leaves Active leaves the bell's count and list at once, before any purge: NewHits gates on Active. The
    // purge is the scan's own per-user statement (CompanyWatchCriterionScanJob, before anything else), a DELETE that names
    // neither flag and leaves the flags of the hits it keeps as they were. A click on the ad afterwards, from a panel that
    // still shows it, is the race the set-based marker exists for (CTO ruling 3): the criterion write reaches no row, the
    // request still answers 204, and the follow source's marks in the same unit of work are committed. Follow
    // notifications are switched off, so the followed ad is a Branschbevakning row in the bell.
    [Fact]
    public async Task An_ad_that_leaves_Active_leaves_the_bell_at_once_and_the_purge_writes_no_flag_and_fails_no_click()
    {
        var (client, userId) = await RegisterUserAsync(factory, "criterion-purge", Ct);
        var watch = await SeedWatchAsync(factory, userId, Ct);
        var criterion = await SeedCriterionAsync(factory, userId, Ct);
        (await SwitchAsync(client, FollowedSegment, false)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var keptUnread = await SeedAdAsync(factory, "Kvar, oläst", Ct);
        var keptRead = await SeedAdAsync(factory, "Kvar, läst", Ct);
        var ended = await SeedAdAsync(factory, "Avslutas", Ct);
        var endedFollowed = await SeedAdAsync(factory, "Avslutas, följd", Ct);
        await SeedHitAsync(factory, userId, endedFollowed, watch, Base.AddMinutes(1), Ct);
        var minute = 10;
        foreach (var ad in new[] { keptUnread, keptRead, ended, endedFollowed })
            await SeedCriterionHitAsync(factory, criterion, ad, Base.AddMinutes(minute++), Ct);
        (await ClickAsync(client, Segment, keptRead)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        Source(await GetFeedAsync(client, Ct), Kind).UnreadCount.ShouldBe(3);

        await ArchiveAsync(factory, ended, Ct);
        await ArchiveAsync(factory, endedFollowed, Ct);

        var beforeThePurge = await GetFeedAsync(client, Ct);
        Source(beforeThePurge, Kind).UnreadCount.ShouldBe(1, "the one unread hit of an ad still Active");
        beforeThePurge.Items.Select(i => i.JobAdId).ShouldBe([keptRead.Value, keptUnread.Value]);

        using var scope = factory.Services.CreateScope();
        var writes = new WriteStatementCapture();
        await using (var db = NewContext(scope, writes))
            (await NewCriterionAdSet.HitsWhoseAdLeftActive(db, userId).ExecuteDeleteAsync(Ct)).ShouldBe(2);

        var purge = writes.Statements.ShouldHaveSingleItem().Text;
        purge.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase).ShouldBeTrue(purge);
        purge.Contains("read_in_notification_feed", StringComparison.OrdinalIgnoreCase).ShouldBeFalse(purge);
        purge.Contains("dismissed_from_notification_feed", StringComparison.OrdinalIgnoreCase).ShouldBeFalse(purge);
        (await CriterionHitRowsAsync(factory, userId, keptRead, Ct)).ShouldHaveSingleItem().ReadInNotificationFeed.ShouldBeTrue();
        (await CriterionHitRowsAsync(factory, userId, keptUnread, Ct)).ShouldHaveSingleItem().ReadInNotificationFeed.ShouldBeFalse();
        (await CriterionHitRowsAsync(factory, userId, endedFollowed, Ct)).ShouldBeEmpty();

        (await ClickAsync(client, Segment, endedFollowed)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await HitRowsAsync(factory, userId, endedFollowed, Ct)).ShouldHaveSingleItem().ReadInNotificationFeed.ShouldBeTrue();
    }

    // ── the wire ─────────────────────────────────────────────────────────────────────────────

    // The DTO is reused unchanged (ADR 0175 D8, security (b)5): a Branschbevakning row carries exactly the keys every row
    // carries, so no criterion id, label, SNI or kommun code and no org.nr reaches the wire, and nothing grades it.
    [Fact]
    public async Task A_criterion_row_carries_exactly_the_keys_every_row_carries_and_no_grade()
    {
        var (client, userId) = await RegisterUserAsync(factory, "criterion-wire", Ct);
        var criterion = await SeedCriterionAsync(factory, userId, Ct);
        var ad = await SeedAdAsync(factory, "Utvecklare", "Bransch AB", Ct);
        await SeedCriterionHitAsync(factory, criterion, ad, Base.AddMinutes(1), Ct);

        var body = await client.GetStringAsync(FeedPath, Ct);

        using var json = JsonDocument.Parse(body);
        var item = json.RootElement.GetProperty("items").EnumerateArray().ShouldHaveSingleItem();
        Names(item).ShouldBe(["companyName", "grade", "isUnread", "jobAdId", "kind", "occurredAt", "title"]);
        item.GetProperty("kind").GetString().ShouldBe(Kind);
        item.GetProperty("jobAdId").GetGuid().ShouldBe(ad.Value);
        item.GetProperty("title").GetString().ShouldBe("Utvecklare");
        item.GetProperty("companyName").GetString().ShouldBe("Bransch AB");
        item.GetProperty("grade").ValueKind.ShouldBe(JsonValueKind.Null);
        var source = json.RootElement.GetProperty("sources").EnumerateArray()
            .Single(s => s.GetProperty("kind").GetString() == Kind);
        Names(source).ShouldBe(["acknowledgedThrough", "kind", "unreadCount"]);
        source.GetProperty("unreadCount").GetInt32().ShouldBe(1);
        body.Contains(criterion.Value.ToString(), StringComparison.OrdinalIgnoreCase).ShouldBeFalse();
    }

    // ── the plan of the per-ad write (ADR 0175 D5, db-migration-writer N2) ───────────────────

    // Every click and X runs the per-ad UPDATE over the user's hits of one ad, and ix_company_watch_criterion_ad_hits_user_jobad
    // exists for it. The statements are the marker's own, captured as EF sends them, then EXPLAINed (never ANALYZEd) in a
    // transaction that is rolled back, with sequential scans priced out as CompanyWatchBrowseQueryPlanTests does: a fixture
    // this small makes a sequential scan the cheapest plan, so what the pin holds is that the statement's shape is one the
    // index serves.
    [Fact]
    public async Task The_per_ad_click_and_X_on_criterion_hits_are_served_by_the_user_jobad_index()
    {
        const string index = "ix_company_watch_criterion_ad_hits_user_jobad";
        var (_, userId) = await RegisterUserAsync(factory, "criterion-explain", Ct);
        var ad = await SeedAdAsync(factory, "J", Ct);
        await SeedCriterionHitAsync(factory, await SeedCriterionAsync(factory, userId, Ct), ad, Base.AddMinutes(1), Ct);

        using var scope = factory.Services.CreateScope();
        var writes = new WriteStatementCapture();
        await using (var db = NewContext(scope, writes))
        {
            var marker = new CompanyWatchCriterionAdFeedSource();
            await marker.MarkRowAsync(new NotificationFeedMarkContext(db, userId), ad, Ct);
            await marker.DismissRowAsync(new NotificationFeedMarkContext(db, userId), ad, Ct);
        }

        writes.Statements.Count.ShouldBe(2, "the click's UPDATE and the X's");
        foreach (var statement in writes.Statements)
        {
            statement.Text.StartsWith("UPDATE company_watch_criterion_ad_hits", StringComparison.OrdinalIgnoreCase)
                .ShouldBeTrue(statement.Text);
            var plan = await ExplainAsync(scope, statement);
            plan.Contains(index, StringComparison.Ordinal).ShouldBeTrue(
                $"The per-ad write is not served by {index}: every click and X would then read the user's whole stock of "
                + $"hits.{Environment.NewLine}Statement:{Environment.NewLine}{statement.Text}{Environment.NewLine}"
                + $"Plan:{Environment.NewLine}{plan}");
        }
    }

    // ── seeding ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Three ads under the criterion: one a match (held by the match source), one a followed company's ad (held by the
    /// follow source), and one the criterion source speaks for. The criterion scan reaches the two held ads after their
    /// producers did, so the newest criterion hit is a held one and the criterion token sits at <paramref name="at"/> + 2 min.
    /// </summary>
    private async Task<(JobAdId HeldByMatch, JobAdId HeldByFollow, JobAdId Own)> SeedHeldTrioAsync(
        Guid userId, CompanyWatchId watch, CompanyWatchCriterionId criterion, DateTimeOffset at)
    {
        var heldByMatch = await SeedAdAsync(factory, "Matchad", Ct);
        var heldByFollow = await SeedAdAsync(factory, "Följd", Ct);
        var own = await SeedAdAsync(factory, "Branschens", Ct);
        await SeedMatchAsync(factory, userId, heldByMatch, at, Ct);
        await SeedHitAsync(factory, userId, heldByFollow, watch, at, Ct);
        await SeedCriterionHitAsync(factory, criterion, own, at, Ct);
        await SeedCriterionHitAsync(factory, criterion, heldByMatch, at.AddMinutes(1), Ct);
        await SeedCriterionHitAsync(factory, criterion, heldByFollow, at.AddMinutes(2), Ct);
        return (heldByMatch, heldByFollow, own);
    }

    // The instant a producer that runs now stamps its row with, in whole seconds so it survives timestamptz unchanged.
    private static DateTimeOffset WholeSecondNow() => DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    // ── the endpoints ────────────────────────────────────────────────────────────────────────

    private static async Task GiveConsentAsync(HttpClient client, bool enabled) =>
        (await client.PutAsJsonAsync("/api/v1/me/background-match-notification-consent", new { enabled }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

    private static Task<HttpResponseMessage> SwitchAsync(HttpClient client, string segment, bool enabled) =>
        client.PutAsJsonAsync($"{FeedPath}/{segment}", new { enabled }, Ct);

    private static Task<HttpResponseMessage> ClickAsync(HttpClient client, string segment, JobAdId ad) =>
        client.PostAsync($"{FeedPath}/{segment}/{ad.Value}/read", content: null, Ct);

    private static Task<HttpResponseMessage> DismissAsync(HttpClient client, string segment, JobAdId ad) =>
        client.PostAsync($"{FeedPath}/{segment}/{ad.Value}/dismiss", content: null, Ct);

    private static Task<HttpResponseMessage> MarkAllAsync(HttpClient client, string segment, DateTimeOffset token) =>
        client.PostAsJsonAsync($"{FeedPath}/{segment}/read", new { throughToken = token.ToString("O") }, Ct);

    /// <summary>A visit to the criterion's page of new ads: the page cuts its window, and the client posts its token back.</summary>
    private static async Task AcknowledgePageAsync(HttpClient client, CompanyWatchCriterionId criterion)
    {
        var page = await client.GetFromJsonAsync<JsonElement>(
            $"/api/v1/me/company-watch-criteria/{criterion.Value}/new-ads", Ct);
        var token = page.GetProperty("acknowledgedThrough").GetString().ShouldNotBeNull();
        (await client.PostAsJsonAsync(
                $"/api/v1/me/company-watch-criteria/{criterion.Value}/new-ads/seen", new { seenThrough = token }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    // ── the probes ───────────────────────────────────────────────────────────────────────────

    private static NotificationFeedReadContext Context(
        AppDbContext db, Guid userId, int maxItems = GetMyNotificationFeedQueryHandler.MaxItems) =>
        new(db, userId, maxItems, Substitute.For<IMatchProfileBuilder>(), Substitute.For<IPerUserJobAdSearchQuery>());

    private static async Task<NotificationFeedDto> HandlerAsync(AppDbContext db, Guid userId)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        return await new GetMyNotificationFeedQueryHandler(
                db, currentUser, Substitute.For<IMatchProfileBuilder>(), Substitute.For<IPerUserJobAdSearchQuery>())
            .Handle(new GetMyNotificationFeedQuery(), Ct);
    }

    private async Task<int> StatementsForOneRequestAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var counter = new StatementCounter();
        await using var db = NewContext(scope, counter);
        await HandlerAsync(db, userId);
        return counter.Count;
    }

    /// <summary>
    /// The statements that name <paramref name="table"/>: exactly the count and the window (the third and fourth
    /// statements, after the gate and the token) when the source reads it, none otherwise; never the token.
    /// </summary>
    private static void AssertReadInTheCountAndTheWindowOnly(List<string> texts, string table, bool reads)
    {
        var naming = texts.Where(t => t.Contains(table, StringComparison.OrdinalIgnoreCase)).ToList();
        if (reads)
            naming.ShouldBe([texts[2], texts[3]], $"the count and the window name {table}, and nothing else does");
        else
            naming.ShouldBeEmpty($"no statement names {table} while its source's gate is off");
    }

    /// <summary>
    /// Each of the user's hit tuples with its xmin, ordered by id. Read with its own connection, outside EF, so nothing
    /// here is cached or tracked.
    /// </summary>
    private async Task<List<string>> TupleVersionsAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        await using var db = NewContext(scope);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            "SELECT id::text || ':' || xmin::text FROM company_watch_criterion_ad_hits WHERE user_id = @user ORDER BY id",
            connection);
        command.Parameters.AddWithValue("user", userId);
        var versions = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync(Ct))
        {
            while (await reader.ReadAsync(Ct))
                versions.Add(reader.GetString(0));
        }

        versions.ShouldNotBeEmpty();
        return versions;
    }

    /// <summary>
    /// EXPLAIN of a captured write, never ANALYZE, inside a transaction that is rolled back, with sequential scans priced
    /// out (the mechanism of <c>CompanyWatchBrowseQueryPlanTests.ExplainMaterialisedAsync</c>).
    /// </summary>
    private static async Task<string> ExplainAsync(IServiceScope scope, WriteStatement statement)
    {
        await using var db = NewContext(scope);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await connection.OpenAsync(Ct);
        await using var transaction = await connection.BeginTransactionAsync(Ct);
        await using (var guc = new NpgsqlCommand("SET LOCAL enable_seqscan = off;", connection, transaction))
            await guc.ExecuteNonQueryAsync(Ct);

        await using var explain = new NpgsqlCommand("EXPLAIN " + statement.Text, connection, transaction);
        explain.Parameters.AddRange(statement.Parameters.Select(p => p.Clone()).ToArray());
        var lines = new List<string>();
        await using (var reader = await explain.ExecuteReaderAsync(Ct))
        {
            while (await reader.ReadAsync(Ct))
                lines.Add(reader.GetString(0));
        }

        await transaction.RollbackAsync(Ct);
        return string.Join(Environment.NewLine, lines);
    }

    private static string[] Names(JsonElement element) =>
        [.. element.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)];
}
