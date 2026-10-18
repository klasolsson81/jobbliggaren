using System.Net;
using System.Net.Http.Json;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Queries.GetNewFollowedCompanyAdCount;
using Jobbliggaren.Application.CompanyWatches.Queries.ListNewFollowedCompanyAds;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.NotificationFeed.Commands.DismissNotification;
using Jobbliggaren.Application.NotificationFeed.Commands.MarkNotificationRead;
using Jobbliggaren.Application.NotificationFeed.Queries.GetMyNotificationFeed;
using Jobbliggaren.Application.NotificationFeed.Sources;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.Matching;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using static Jobbliggaren.Api.IntegrationTests.NotificationFeed.NotificationFeedTestSeed;

namespace Jobbliggaren.Api.IntegrationTests.NotificationFeed;

/// <summary>
/// #2158 — an ad is ONE notification in the bell. While match notifications reach the bell, an ad the user has any match
/// row for is that match row, and the follow source leaves the ad out of its count, its window and both fallbacks; a click
/// or an X reaches the ad in every source, each under its own gate. Against Testcontainers Postgres and the production
/// endpoints.
/// <para>
/// <see cref="Expect"/> is the rule as a model, each scenario moves the model and the
/// server in step, every read of the feed is compared with the model, and every write is read back from the rows. Consent
/// changes only through its own endpoint. Every user is fresh (the shared database is never reset) and reads the feed at
/// most seven times, inside the feed's read bucket.
/// </para>
/// </summary>
[Collection("Api")]
public class NotificationFeedOneNotificationPerAdTests(ApiFactory factory)
{
    private const string Match = "Match";
    private const string Followed = "FollowedCompanyAd";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ── the model ────────────────────────────────────────────────────────────────────────────

    private sealed class Row(DateTimeOffset at)
    {
        public DateTimeOffset At { get; } = at;
        public bool Read { get; set; }
        public bool Dismissed { get; set; }
    }

    private sealed class Ad(JobAdId id)
    {
        public JobAdId Id { get; } = id;
        public Row? Match { get; set; }
        public List<Row> Hits { get; } = [];
    }

    private sealed class World
    {
        public bool Consent { get; set; }
        public DateTimeOffset? MatchesOffAt { get; set; }
        public DateTimeOffset? FollowsOffAt { get; set; }
        public DateTimeOffset? MatchSeen { get; set; }
        public DateTimeOffset? FollowSeen { get; set; }
        public SortedDictionary<string, Ad> Ads { get; } = new(StringComparer.Ordinal);
        public bool Gate => Consent && MatchesOffAt is null;
    }

    private sealed record Expected(
        IReadOnlyList<(string Ad, string Kind, bool Unread)> Rows,
        int MatchUnread,
        DateTimeOffset? MatchToken,
        int FollowUnread,
        DateTimeOffset? FollowToken,
        int OversiktFollowed,
        int NewMatches);

    private static bool IsNew(DateTimeOffset at, DateTimeOffset? seen) => seen is not { } through || at > through;

    private static DateTimeOffset Later(DateTimeOffset? current, DateTimeOffset candidate) =>
        current is { } c && c >= candidate ? c : candidate;

    /// <summary>
    /// The bell for one user, as the rule says it must be. The match source speaks while its gate holds (consent and the
    /// bell's match switch) and lists every match row not taken out with the X; the follow source speaks while its own
    /// switch is on, and an ad the match source holds (ANY match row, whatever its state) is no follow notification. The
    /// follow token is the newest of every new hit, held or not; unread is new and not read in the bell; Översikt's two
    /// counts read none of the bell's state.
    /// </summary>
    private static Expected Expect(World w)
    {
        var rows = new List<(string, string, bool)>();
        int matchUnread = 0, followUnread = 0;
        DateTimeOffset? matchToken = null, followToken = null;
        foreach (var (name, ad) in w.Ads)
        {
            if (w.Gate && ad.Match is { } match)
            {
                var isNew = IsNew(match.At, w.MatchSeen);
                if (isNew)
                    matchToken = Later(matchToken, match.At);
                var unread = isNew && !match.Read;
                if (unread)
                    matchUnread++;
                if (!match.Dismissed)
                    rows.Add((name, Match, unread));
            }

            if (w.FollowsOffAt is null && ad.Hits.Count > 0)
            {
                foreach (var hit in ad.Hits.Where(h => IsNew(h.At, w.FollowSeen)))
                    followToken = Later(followToken, hit.At);
                if (w.Gate && ad.Match is not null)
                    continue;
                var unread = ad.Hits.Any(h => IsNew(h.At, w.FollowSeen) && !h.Read);
                if (unread)
                    followUnread++;
                if (ad.Hits.Any(h => !h.Dismissed))
                    rows.Add((name, Followed, unread));
            }
        }

        return new Expected(
            rows, matchUnread, matchToken, followUnread, followToken,
            OversiktFollowed: w.Ads.Values.Count(a => a.Hits.Any(h => IsNew(h.At, w.FollowSeen))),
            NewMatches: w.Ads.Values.Count(a => a.Match is { } m && IsNew(m.At, w.MatchSeen)));
    }

    /// <summary>What the client does with the answer it holds when a click or an X is confirmed (use-notification-feed.ts).</summary>
    private static FeedBody ClientPatch(FeedBody feed, string kind, Guid jobAdId, bool dismissed)
    {
        var row = feed.Items.SingleOrDefault(i => i.Kind == kind && i.JobAdId == jobAdId);
        if (row is null || (!dismissed && !row.IsUnread))
            return feed;
        List<FeedItem> items = dismissed
            ? [.. feed.Items.Where(i => i != row)]
            : [.. feed.Items.Select(i => i == row ? i with { IsUnread = false } : i)];
        List<FeedSource> sources =
        [
            .. feed.Sources.Select(s => row.IsUnread && s.Kind == kind && s.UnreadCount is > 0
                ? s with { UnreadCount = s.UnreadCount - 1 }
                : s),
        ];
        return new FeedBody(items, sources);
    }

    private static List<(string Kind, Guid JobAdId, bool IsUnread)> ItemsOf(FeedBody feed) =>
        [.. feed.Items.Select(i => (i.Kind, i.JobAdId, i.IsUnread)).OrderBy(i => i.Kind, StringComparer.Ordinal).ThenBy(i => i.JobAdId)];

    // ── the driver ───────────────────────────────────────────────────────────────────────────

    private sealed class Scenario(ApiFactory factory, HttpClient client, Guid userId, CompanyWatchId watch)
    {
        private readonly List<(string Kind, Guid JobAdId, bool Dismissed)> _patches = [];
        private bool _patchable = true;

        public World Model { get; } = new();

        public FeedBody? Shown { get; private set; }

        public async Task AdAsync(string name) => Model.Ads[name] = new Ad(await SeedAdAsync(factory, name, Ct));

        public async Task MatchAsync(string name, DateTimeOffset at)
        {
            await SeedMatchAsync(factory, userId, Model.Ads[name].Id, at, Ct);
            Model.Ads[name].Match = new Row(at);
            _patchable = false;
        }

        public async Task<CompanyWatchId> WatchAsync() => await SeedWatchAsync(factory, userId, Ct);

        public async Task HitAsync(string name, DateTimeOffset at, CompanyWatchId? through = null)
        {
            await SeedHitAsync(factory, userId, Model.Ads[name].Id, through ?? watch, at, Ct);
            Model.Ads[name].Hits.Add(new Row(at));
            _patchable = false;
        }

        /// <summary>Reads the feed and holds it against the model, and against the client's own patch of the last answer.</summary>
        public async Task ReadFeedAsync()
        {
            var feed = await GetFeedAsync(client, Ct);
            var expected = Expect(Model);
            var names = Model.Ads.ToDictionary(p => p.Value.Id.Value, p => p.Key);

            feed.Items.Select(i => i.JobAdId).Distinct().Count().ShouldBe(feed.Items.Count, "one notification per ad");
            feed.Items
                .Select(i => (Ad: names[i.JobAdId], i.Kind, Unread: i.IsUnread))
                .OrderBy(r => r.Ad, StringComparer.Ordinal)
                .ShouldBe(expected.Rows.OrderBy(r => r.Ad, StringComparer.Ordinal));
            Source(feed, Match).ShouldBe(new FeedSource(Match, expected.MatchUnread, expected.MatchToken));
            Source(feed, Followed).ShouldBe(new FeedSource(Followed, expected.FollowUnread, expected.FollowToken));
            (feed.Sources.Sum(s => s.UnreadCount ?? 0) - feed.Items.Count(i => i.IsUnread))
                .ShouldBe(0, "the client's not-in-list line: every unread notification is in the list");

            if (_patchable && _patches.Count > 0 && Shown is { } before)
            {
                var patched = _patches.Aggregate(before, (f, p) => ClientPatch(f, p.Kind, p.JobAdId, p.Dismissed));
                ItemsOf(feed).ShouldBe(ItemsOf(patched), "the client's patch of the answer it held");
                feed.Sources.ShouldBe(patched.Sources, "the client's patch of the counts it held");
            }

            _patches.Clear();
            _patchable = true;
            Shown = feed;
        }

        public async Task OversiktAsync()
        {
            var expected = Expect(Model);
            (await CountAsync(client, "/api/v1/me/followed-company-ads/new-count", Ct)).ShouldBe(expected.OversiktFollowed);
            (await CountAsync(client, "/api/v1/me/new-match-count", Ct)).ShouldBe(expected.NewMatches);
        }

        /// <summary>A click on the row the last answer listed as unread, through the segment of the kind it was listed under.</summary>
        public async Task ClickAsync(string name)
        {
            var row = ShownRow(name);
            row.IsUnread.ShouldBeTrue($"{name} is clicked only while it is listed as unread, as the bell does");
            (await client.PostAsync($"{FeedPath}/{Segment(row.Kind)}/{row.JobAdId}/read", content: null, Ct))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);

            var ad = Model.Ads[name];
            if (ad.Match is { } match && Model.Consent)
                match.Read = true;
            foreach (var hit in ad.Hits)
                hit.Read = true;
            _patches.Add((row.Kind, row.JobAdId, false));
            await AssertRowsAsync();
        }

        public async Task DismissAsync(string name)
        {
            var row = ShownRow(name);
            (await client.PostAsync($"{FeedPath}/{Segment(row.Kind)}/{row.JobAdId}/dismiss", content: null, Ct))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);

            var ad = Model.Ads[name];
            if (ad.Match is { } match && Model.Consent)
                (match.Read, match.Dismissed) = (true, true);
            foreach (var hit in ad.Hits)
                (hit.Read, hit.Dismissed) = (true, true);
            _patches.Add((row.Kind, row.JobAdId, true));
            await AssertRowsAsync();
        }

        /// <summary>"Markera alla som lästa" with the tokens of the last answer, sent only while that answer counts something.</summary>
        public async Task MarkAllAsync()
        {
            var shown = Shown.ShouldNotBeNull();
            shown.Sources.Sum(s => s.UnreadCount.ShouldNotBeNull()).ShouldBeGreaterThan(0, "the button shows only above zero");
            var matchToken = Source(shown, Match).AcknowledgedThrough;
            var followToken = Source(shown, Followed).AcknowledgedThrough;
            if (matchToken is { } mt)
                (await client.PostAsJsonAsync($"{FeedPath}/matches/read", new { throughToken = mt.ToString("O") }, Ct))
                    .StatusCode.ShouldBe(HttpStatusCode.NoContent);
            if (followToken is { } ft)
                (await client.PostAsJsonAsync($"{FeedPath}/followed-company-ads/read", new { throughToken = ft.ToString("O") }, Ct))
                    .StatusCode.ShouldBe(HttpStatusCode.NoContent);

            foreach (var ad in Model.Ads.Values)
            {
                if (matchToken is { } through && Model.Consent && ad.Match is { } match
                    && match.At <= through && IsNew(match.At, Model.MatchSeen))
                    match.Read = true;
                if (followToken is { } hitsThrough)
                    foreach (var hit in ad.Hits.Where(h => h.At <= hitsThrough && IsNew(h.At, Model.FollowSeen)))
                        hit.Read = true;
            }

            _patchable = false;
            await AssertRowsAsync();
        }

        /// <summary>The bell's match switch. Switching on first marks what arrived while it was off as read, under the consent.</summary>
        public async Task MatchesAsync(bool on)
        {
            var before = DateTimeOffset.UtcNow;
            (await client.PutAsJsonAsync($"{FeedPath}/matches", new { enabled = on }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            if (on)
            {
                if (Model.MatchesOffAt is { } offAt && Model.Consent)
                    foreach (var match in Model.Ads.Values.Select(a => a.Match).OfType<Row>().Where(m => m.At > offAt))
                        match.Read = true;
                Model.MatchesOffAt = null;
            }
            else
            {
                Model.MatchesOffAt ??= before;
            }

            _patchable = false;
            await AssertRowsAsync();
        }

        public async Task FollowsAsync(bool on)
        {
            var before = DateTimeOffset.UtcNow;
            (await client.PutAsJsonAsync($"{FeedPath}/followed-company-ads", new { enabled = on }, Ct))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);
            if (on)
            {
                if (Model.FollowsOffAt is { } offAt)
                    foreach (var hit in Model.Ads.Values.SelectMany(a => a.Hits).Where(h => h.At > offAt))
                        hit.Read = true;
                Model.FollowsOffAt = null;
            }
            else
            {
                Model.FollowsOffAt ??= before;
            }

            _patchable = false;
            await AssertRowsAsync();
        }

        /// <summary>The background-match consent, through its own endpoint and nothing else.</summary>
        public async Task ConsentAsync(bool enabled)
        {
            await GiveConsentAsync(client, enabled);
            Model.Consent = enabled;
            _patchable = false;
            await AssertRowsAsync();
        }

        public async Task SeeMatchesThroughAsync(DateTimeOffset through)
        {
            (await client.PostAsJsonAsync("/api/v1/me/matches/seen", new { seenThrough = through }, Ct))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);
            Model.MatchSeen = Later(Model.MatchSeen, through);
            _patchable = false;
            await AssertRowsAsync();
        }

        public async Task SeeFollowedAdsThroughAsync(DateTimeOffset through)
        {
            (await client.PostAsJsonAsync("/api/v1/me/followed-company-ads/seen", new { seenThrough = through }, Ct))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);
            Model.FollowSeen = Later(Model.FollowSeen, through);
            _patchable = false;
            await AssertRowsAsync();
        }

        private FeedItem ShownRow(string name)
        {
            var shown = Shown.ShouldNotBeNull();
            return shown.Items.Single(i => i.JobAdId == Model.Ads[name].Id.Value);
        }

        private static string Segment(string kind) => kind == Match ? "matches" : "followed-company-ads";

        /// <summary>Both rows of every ad, read back, against the model: what each write reached and what it left alone.</summary>
        private async Task AssertRowsAsync()
        {
            foreach (var (name, ad) in Model.Ads)
            {
                if (ad.Match is { } match)
                {
                    var row = await MatchRowAsync(factory, userId, ad.Id, Ct);
                    (row.ReadInNotificationFeed, row.DismissedFromNotificationFeed)
                        .ShouldBe((match.Read, match.Dismissed), $"{name}'s match row");
                }

                if (ad.Hits.Count > 0)
                {
                    var hits = await HitRowsAsync(factory, userId, ad.Id, Ct);
                    hits.Select(h => (h.CreatedAt, h.ReadInNotificationFeed, h.DismissedFromNotificationFeed)).OrderBy(h => h.CreatedAt)
                        .ShouldBe(ad.Hits.Select(h => (h.At, h.Read, h.Dismissed)).OrderBy(h => h.At), $"{name}'s hits");
                }
            }
        }
    }

    private async Task<Scenario> StartAsync(string prefix, bool onlyMatched, bool consent)
    {
        var (client, userId) = await RegisterUserAsync(factory, prefix, Ct);
        // An "endast matchade" watch puts the follow source on its grade path. The user states no occupation, so the
        // filter is inert and admits every hit: the path runs, and the rule above holds unchanged on it.
        var watch = await SeedWatchAsync(factory, userId, onlyMatched, Ct);
        var scenario = new Scenario(factory, client, userId, watch);
        if (consent)
            await scenario.ConsentAsync(true);
        return scenario;
    }

    // ── Tier A: through the endpoints ────────────────────────────────────────────────────────

    [Fact]
    public async Task A_match_that_is_no_followed_ad_is_its_own_notification_through_a_click_and_an_X()
    {
        var s = await StartAsync("one-match-only", onlyMatched: false, consent: true);
        await s.AdAsync("M");
        await s.MatchAsync("M", Base.AddMinutes(1));

        await s.ReadFeedAsync();
        await s.ClickAsync("M");
        await s.ReadFeedAsync();
        await s.DismissAsync("M");
        await s.ReadFeedAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_followed_ad_that_is_no_match_is_its_own_notification(bool onlyMatched)
    {
        var s = await StartAsync("one-follow-only", onlyMatched, consent: true);
        await s.AdAsync("F");
        await s.HitAsync("F", Base.AddMinutes(1));

        await s.ReadFeedAsync();
        await s.OversiktAsync();
        await s.ClickAsync("F");
        await s.ReadFeedAsync();
        await s.DismissAsync("F");
        await s.ReadFeedAsync();
    }

    // J, D and K are each a match and a followed company's new ad; K's hit is the newest hit of all, so a follow token
    // narrowed to the ads the follow source shows would fall short of it. F is only followed, M only a match.
    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public async Task An_ad_that_is_both_is_one_notification_the_match_row_through_every_write_and_both_switches(
        bool onlyMatched, bool matchFirst)
    {
        var s = await StartAsync("one-both", onlyMatched, consent: true);
        var (matchAt, hitAt) = matchFirst ? (Base.AddMinutes(1), Base.AddMinutes(20)) : (Base.AddMinutes(20), Base.AddMinutes(1));
        var i = 0;
        foreach (var name in new[] { "J", "D", "K" })
        {
            await s.AdAsync(name);
            await s.MatchAsync(name, matchAt.AddMinutes(i));
            await s.HitAsync(name, hitAt.AddMinutes(i));
            i++;
        }

        await s.AdAsync("F");
        await s.HitAsync("F", Base);
        await s.AdAsync("M");
        await s.MatchAsync("M", Base.AddMinutes(30));

        await s.ReadFeedAsync();
        await s.OversiktAsync();
        await s.ClickAsync("J");
        await s.ReadFeedAsync();
        await s.DismissAsync("D");
        await s.ReadFeedAsync();
        await s.MatchesAsync(false);
        await s.ReadFeedAsync();
        await s.MatchesAsync(true);
        await s.ReadFeedAsync();
        await s.MarkAllAsync();
        await s.ReadFeedAsync();
        await s.MatchesAsync(false);
        await s.ReadFeedAsync();
        await s.OversiktAsync();
    }

    // The hit is read first and the match row is born after a consent grant (the rescan's order): the ad comes back as
    // an unread match. Named in the decision as a residual.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_followed_ad_read_first_returns_as_an_unread_match_when_its_match_row_is_born_later(bool onlyMatched)
    {
        var s = await StartAsync("one-hit-first", onlyMatched, consent: false);
        await s.AdAsync("J");
        await s.HitAsync("J", Base.AddMinutes(1));

        await s.ReadFeedAsync();
        await s.ClickAsync("J");
        await s.ReadFeedAsync();
        await s.ConsentAsync(true);
        await s.MatchAsync("J", Base.AddMinutes(10));
        await s.ReadFeedAsync();
        await s.DismissAsync("J");
        await s.ReadFeedAsync();
    }

    // The common order: the match is read within minutes, the hit is born at night. It never rings while the match
    // source speaks, and comes back by its own state when match notifications are switched off (Klas, 2026-10-17).
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_hit_born_after_its_match_was_read_never_rings_while_matches_reach_the_bell(bool onlyMatched)
    {
        var s = await StartAsync("one-read-then-hit", onlyMatched, consent: true);
        await s.AdAsync("J");
        await s.MatchAsync("J", Base.AddMinutes(1));

        await s.ReadFeedAsync();
        await s.ClickAsync("J");
        await s.HitAsync("J", Base.AddMinutes(10));
        await s.ReadFeedAsync();
        await s.MatchesAsync(false);
        await s.ReadFeedAsync();
        await s.MatchesAsync(true);
        await s.ReadFeedAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_hit_born_after_its_match_was_taken_out_with_the_X_stays_out(bool onlyMatched)
    {
        var s = await StartAsync("one-x-then-hit", onlyMatched, consent: true);
        await s.AdAsync("J");
        await s.MatchAsync("J", Base.AddMinutes(1));

        await s.ReadFeedAsync();
        await s.DismissAsync("J");
        await s.HitAsync("J", Base.AddMinutes(10));
        await s.ReadFeedAsync();
        await s.MatchesAsync(false);
        await s.ReadFeedAsync();
    }

    // A match row older than the match watermark still holds its ad; a list visit moves neither the bell's flags nor this.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_match_row_behind_the_watermark_still_holds_its_ad(bool onlyMatched)
    {
        var s = await StartAsync("one-watermarks", onlyMatched, consent: true);
        await s.AdAsync("J");
        await s.MatchAsync("J", Base.AddMinutes(1));
        await s.SeeMatchesThroughAsync(Base.AddMinutes(2));

        await s.ReadFeedAsync();
        await s.HitAsync("J", Base.AddMinutes(10));
        await s.ReadFeedAsync();
        await s.SeeFollowedAdsThroughAsync(Base.AddMinutes(11));
        await s.ReadFeedAsync();
        await s.OversiktAsync();
    }

    [Fact]
    public async Task A_click_or_an_X_on_the_match_row_reaches_the_ads_hits_through_both_of_its_watches()
    {
        var s = await StartAsync("one-two-watches", onlyMatched: false, consent: true);
        var second = await s.WatchAsync();
        foreach (var (name, n) in new[] { ("J1", 0), ("J2", 1) })
        {
            await s.AdAsync(name);
            await s.MatchAsync(name, Base.AddMinutes(1 + n));
            await s.HitAsync(name, Base.AddMinutes(10 + n));
            await s.HitAsync(name, Base.AddMinutes(20 + n), through: second);
        }

        await s.ReadFeedAsync();
        await s.OversiktAsync();
        await s.ClickAsync("J1");
        await s.DismissAsync("J2");
        await s.ReadFeedAsync();
        await s.MatchesAsync(false);
        await s.ReadFeedAsync();
    }

    // With the match switch off and the consent held, a click or an X on the follow row reaches the match row too: the
    // positive control for the withdrawn-consent case below.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task With_the_match_switch_off_a_click_or_an_X_on_the_follow_row_reaches_the_match_row(bool onlyMatched)
    {
        var s = await StartAsync("one-switch-off", onlyMatched, consent: true);
        foreach (var (name, n) in new[] { ("J1", 0), ("J2", 1) })
        {
            await s.AdAsync(name);
            await s.MatchAsync(name, Base.AddMinutes(1 + n));
            await s.HitAsync(name, Base.AddMinutes(10 + n));
        }

        await s.MatchesAsync(false);
        await s.ReadFeedAsync();
        await s.ClickAsync("J1");
        await s.DismissAsync("J2");
        await s.ReadFeedAsync();
        await s.MatchesAsync(true);
        await s.ReadFeedAsync();
    }

    // The consent, withdrawn through its own endpoint: the follow rows speak, a click or an X writes the hits and never the
    // match row, and the gate is the user's own (another user's consent holds nothing for this one).
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task With_the_consent_withdrawn_the_follow_rows_speak_and_no_write_reaches_a_match_row(bool onlyMatched)
    {
        var (other, _) = await RegisterUserAsync(factory, "one-consent-other", Ct);
        await GiveConsentAsync(other, true);
        var s = await StartAsync("one-consent", onlyMatched, consent: true);
        foreach (var (name, n) in new[] { ("J1", 0), ("J2", 1) })
        {
            await s.AdAsync(name);
            await s.MatchAsync(name, Base.AddMinutes(1 + n));
            await s.HitAsync(name, Base.AddMinutes(10 + n));
        }

        await s.ReadFeedAsync();
        await s.ConsentAsync(false);
        await s.ReadFeedAsync();
        await s.OversiktAsync();
        await s.ClickAsync("J1");
        await s.DismissAsync("J2");
        await s.ReadFeedAsync();
        await s.ConsentAsync(true);
        await s.ReadFeedAsync();
    }

    // The follow switch is no gate on a write: a click on the match row reads the ad's hits while the follows are off.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task With_the_follow_switch_off_a_click_on_the_match_row_still_reads_its_hits(bool onlyMatched)
    {
        var s = await StartAsync("one-follows-off", onlyMatched, consent: true);
        await s.AdAsync("J");
        await s.MatchAsync("J", Base.AddMinutes(1));
        await s.HitAsync("J", Base.AddMinutes(10));
        await s.AdAsync("F");
        await s.HitAsync("F", Base.AddMinutes(11));

        await s.FollowsAsync(false);
        await s.ReadFeedAsync();
        await s.OversiktAsync();
        await s.ClickAsync("J");
        await s.ReadFeedAsync();
        await s.FollowsAsync(true);
        await s.ReadFeedAsync();
        await s.MatchesAsync(false);
        await s.ReadFeedAsync();
    }

    [Fact]
    public async Task Another_users_match_row_holds_nothing_for_this_user()
    {
        var (other, otherId) = await RegisterUserAsync(factory, "one-isolation-other", Ct);
        await GiveConsentAsync(other, true);
        var s = await StartAsync("one-isolation", onlyMatched: false, consent: true);
        await s.AdAsync("J");
        await s.HitAsync("J", Base.AddMinutes(10));
        await SeedMatchAsync(factory, otherId, s.Model.Ads["J"].Id, Base.AddMinutes(1), Ct);

        await s.ReadFeedAsync();
    }

    // Matches switched back on: the backlog mark reads a match row born while they were off, and its ad's unread follow
    // notification from that period goes with it. Named in the decision as a residual.
    [Fact]
    public async Task Switching_matches_back_on_takes_the_ad_as_a_read_match()
    {
        var s = await StartAsync("one-switch-on", onlyMatched: false, consent: true);
        await s.MatchesAsync(false);
        var later = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds());
        await s.AdAsync("J");
        await s.MatchAsync("J", later);
        await s.HitAsync("J", later.AddMinutes(1));

        await s.ReadFeedAsync();
        await s.MatchesAsync(true);
        await s.ReadFeedAsync();
    }

    // "Markera alla" while matches are off sends no match token, so the match row is still unread when they are back on.
    // Named in the decision as a residual.
    [Fact]
    public async Task Mark_all_while_matches_are_off_leaves_the_match_row_unread_for_when_they_are_back_on()
    {
        var s = await StartAsync("one-mark-all-off", onlyMatched: false, consent: true);
        await s.AdAsync("J");
        await s.MatchAsync("J", Base.AddMinutes(1));
        await s.HitAsync("J", Base.AddMinutes(10));

        await s.MatchesAsync(false);
        await s.ReadFeedAsync();
        await s.MarkAllAsync();
        await s.ReadFeedAsync();
        await s.MatchesAsync(true);
        await s.ReadFeedAsync();
    }

    // ── Tier B: the follow source on Npgsql, with a small window ─────────────────────────────

    // Held hits take no slot in the window and move no horizon: seven held ads with the newest hits, a read ad and an
    // unread one behind them, and a window of six hits (MaxItems 2). The narrowing runs in SQL before Take.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Held_hits_take_no_window_slot_and_move_no_horizon(bool onlyMatched)
    {
        var (client, userId) = await RegisterUserAsync(factory, "held-window", Ct);
        await GiveConsentAsync(client, true);
        var watch = await SeedWatchAsync(factory, userId, onlyMatched, Ct);
        for (var n = 1; n <= 7; n++)
        {
            var held = await SeedAdAsync(factory, $"Hållen {n}", Ct);
            await SeedMatchAsync(factory, userId, held, Base.AddMinutes(60 + n), Ct);
            await SeedHitAsync(factory, userId, held, watch, Base.AddMinutes(60 + n), Ct);
        }

        var read = await SeedAdAsync(factory, "Läst", Ct);
        await SeedHitAsync(factory, userId, read, watch, Base.AddMinutes(30), Ct);
        await ReadHitsInBellAsync(factory, userId, read, Ct);
        var unread = await SeedAdAsync(factory, "Oläst", Ct);
        await SeedHitAsync(factory, userId, unread, watch, Base.AddMinutes(20), Ct);

        using var scope = factory.Services.CreateScope();
        var counter = new StatementCounter();
        await using var db = NewContext(scope, counter);
        var context = new NotificationFeedReadContext(db, userId, MaxItems: 2, OccupationProfile(), GradeEverythingAsMatching());

        await new MatchFeedSource().ReadAsync(context, Ct);
        var followed = await new FollowedCompanyAdFeedSource().ReadAsync(context, Ct);

        followed.Items.Select(i => (i.JobAdId, i.IsUnread)).ShouldBe([(read.Value, false), (unread.Value, true)]);
        followed.Horizon.ShouldBeNull();
        followed.UnreadCount.ShouldBe(1);
        followed.AcknowledgedThrough.ShouldBe(Base.AddMinutes(67), "the token is the newest new hit, held or not");
        counter.Count.ShouldBe(9, "the narrowing sits inside the existing statements");
    }

    // The common-path fallback: read rows fill the window, and the grouped statement that names the unread ad behind them
    // never names a held one.
    [Fact]
    public async Task On_the_common_path_the_grouped_statement_names_no_ad_the_match_source_holds()
    {
        var (userId, held, unread) = await SeedCrowdedWindowAsync("held-grouped", onlyMatched: false);

        using var scope = factory.Services.CreateScope();
        await using var db = NewContext(scope);
        var read = await new FollowedCompanyAdFeedSource().ReadAsync(
            new NotificationFeedReadContext(db, userId, MaxItems: 2, OccupationProfile(), GradeEverythingAsMatching()), Ct);

        read.UnreadCount.ShouldBe(1);
        read.Items.Where(i => i.IsUnread).Select(i => i.JobAdId).ShouldBe([unread.Value]);
        read.Items.ShouldNotContain(i => i.JobAdId == held.Value);
    }

    // The grade-path fallback: its display read takes no more hits than the count loaded, so a held hit inside the time
    // range it reads would push the wanted ad out. And no held ad is ever sent to the grader.
    [Fact]
    public async Task On_the_grade_path_a_held_ad_is_neither_counted_graded_nor_listed_and_never_crowds_the_display_read()
    {
        var (userId, held, unread) = await SeedCrowdedWindowAsync("held-grade", onlyMatched: true);
        var search = GradeEverythingAsMatching();

        using var scope = factory.Services.CreateScope();
        await using var db = NewContext(scope);
        var read = await new FollowedCompanyAdFeedSource().ReadAsync(
            new NotificationFeedReadContext(db, userId, MaxItems: 2, OccupationProfile(), search), Ct);

        read.UnreadCount.ShouldBe(1);
        read.Items.Where(i => i.IsUnread).Select(i => (i.JobAdId, i.Title)).ShouldBe([(unread.Value, "Oläst")]);
        read.Items.ShouldNotContain(i => i.JobAdId == held.Value);
        await search.Received(1).FilterToMatchingAsync(
            Arg.Any<FullCandidateMatchProfile>(), Arg.Any<IReadOnlyCollection<JobAdId>>(), Arg.Any<CancellationToken>());
        await search.DidNotReceive().FilterToMatchingAsync(
            Arg.Any<FullCandidateMatchProfile>(),
            Arg.Is<IReadOnlyCollection<JobAdId>>(ids => ids.Contains(held)),
            Arg.Any<CancellationToken>());
    }

    // The grade path's count bound reads the narrowed hits: two held hits do not push one unread hit past a bound of two.
    [Fact]
    public async Task The_count_bound_counts_no_held_hit()
    {
        var (client, userId) = await RegisterUserAsync(factory, "held-bound", Ct);
        await GiveConsentAsync(client, true);
        var watch = await SeedWatchAsync(factory, userId, onlyMatched: true, Ct);
        for (var n = 1; n <= 2; n++)
        {
            var held = await SeedAdAsync(factory, $"Hållen {n}", Ct);
            await SeedMatchAsync(factory, userId, held, Base.AddMinutes(n), Ct);
            await SeedHitAsync(factory, userId, held, watch, Base.AddMinutes(10 + n), Ct);
        }

        var unread = await SeedAdAsync(factory, "Oläst", Ct);
        await SeedHitAsync(factory, userId, unread, watch, Base.AddMinutes(5), Ct);

        using var scope = factory.Services.CreateScope();
        await using var db = NewContext(scope);
        var read = await new FollowedCompanyAdFeedSource().ReadBoundedAsync(
            new NotificationFeedReadContext(db, userId, MaxItems: 20, OccupationProfile(), GradeEverythingAsMatching()),
            maxCountedHits: 2, Ct);

        read.UnreadCount.ShouldBe(1);
        read.Items.Select(i => i.JobAdId).ShouldBe([unread.Value]);
    }

    // The gate is checked inside each statement that reads a match row: consent withdrawn through the endpoint between the
    // follow token and the count, and the count and the window already speak for the follow row.
    [Fact]
    public async Task The_match_gate_is_checked_in_each_statement_that_reads_a_match_row()
    {
        var (other, _) = await RegisterUserAsync(factory, "held-race-other", Ct);
        await GiveConsentAsync(other, true);
        var (client, userId) = await RegisterUserAsync(factory, "held-race", Ct);
        await GiveConsentAsync(client, true);
        var watch = await SeedWatchAsync(factory, userId, Ct);
        var ad = await SeedAdAsync(factory, "J", Ct);
        await SeedMatchAsync(factory, userId, ad, Base.AddMinutes(1), Ct);
        await SeedHitAsync(factory, userId, ad, watch, Base.AddMinutes(10), Ct);

        using var scope = factory.Services.CreateScope();
        var withdraw = new AfterFirstMaxInterceptor(() => GiveConsentAsync(client, false));
        await using var db = NewContext(scope, withdraw);
        var read = await new FollowedCompanyAdFeedSource().ReadAsync(
            new NotificationFeedReadContext(db, userId, MaxItems: 20, OccupationProfile(), GradeEverythingAsMatching()), Ct);

        withdraw.Fired.ShouldBeTrue();
        read.UnreadCount.ShouldBe(1);
        var item = read.Items.ShouldHaveSingleItem();
        item.JobAdId.ShouldBe(ad.Value);
        item.IsUnread.ShouldBeTrue();
    }

    // ── the SQL itself ───────────────────────────────────────────────────────────────────────

    // The dedup is the bell's alone: Översikt's followed-ads count and its list never read a match row.
    [Fact]
    public async Task Oversikts_followed_count_and_the_followed_list_never_read_a_match_row()
    {
        var (client, userId) = await RegisterUserAsync(factory, "held-sql-oversikt", Ct);
        await GiveConsentAsync(client, true);
        var watch = await SeedWatchAsync(factory, userId, Ct);
        var ad = await SeedAdAsync(factory, "J", Ct);
        await SeedMatchAsync(factory, userId, ad, Base.AddMinutes(1), Ct);
        await SeedHitAsync(factory, userId, ad, watch, Base.AddMinutes(10), Ct);

        using var scope = factory.Services.CreateScope();
        var statements = new StatementText();
        await using var db = NewContext(scope, statements);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);

        var count = await new GetNewFollowedCompanyAdCountQueryHandler(db, currentUser, OccupationProfile(), GradeEverythingAsMatching())
            .Handle(new GetNewFollowedCompanyAdCountQuery(), Ct);
        await new ListNewFollowedCompanyAdsQueryHandler(db, currentUser, OccupationProfile(), GradeEverythingAsMatching())
            .Handle(new ListNewFollowedCompanyAdsQuery(), Ct);

        count.Count.ShouldBe(1, "Översikt still counts the ad the bell shows as a match");
        statements.Texts.ShouldNotBeEmpty();
        statements.Texts.ShouldNotContain(t => t.Contains("user_job_ad_matches", StringComparison.OrdinalIgnoreCase));
    }

    // Which follow statements read a match row: the count and the window while the gate holds (the control), the token
    // never, and none at all while the consent is withdrawn, the match switch is off or the follow switch is off (a gated
    // source reads no row).
    [Theory]
    [InlineData("gate-on")]
    [InlineData("consent-off")]
    [InlineData("matches-off")]
    [InlineData("follows-off")]
    public async Task The_follow_source_reads_a_match_row_only_while_the_match_gate_holds(string state)
    {
        var (other, _) = await RegisterUserAsync(factory, $"held-sql-{state}-other", Ct);
        await GiveConsentAsync(other, true);
        var (client, userId) = await RegisterUserAsync(factory, $"held-sql-{state}", Ct);
        await GiveConsentAsync(client, true);
        var watch = await SeedWatchAsync(factory, userId, Ct);
        var ad = await SeedAdAsync(factory, "J", Ct);
        await SeedMatchAsync(factory, userId, ad, Base.AddMinutes(1), Ct);
        await SeedHitAsync(factory, userId, ad, watch, Base.AddMinutes(10), Ct);
        if (state == "consent-off")
            await GiveConsentAsync(client, false);
        if (state == "matches-off")
            (await client.PutAsJsonAsync($"{FeedPath}/matches", new { enabled = false }, Ct))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        if (state == "follows-off")
            (await client.PutAsJsonAsync($"{FeedPath}/followed-company-ads", new { enabled = false }, Ct))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var scope = factory.Services.CreateScope();
        var statements = new StatementText();
        await using var db = NewContext(scope, statements);
        await new FollowedCompanyAdFeedSource().ReadAsync(
            new NotificationFeedReadContext(db, userId, MaxItems: 20, OccupationProfile(), GradeEverythingAsMatching()), Ct);

        statements.Texts.ShouldNotBeEmpty();
        var naming = statements.Texts.Where(t => t.Contains("user_job_ad_matches", StringComparison.OrdinalIgnoreCase)).ToList();
        if (state == "gate-on")
        {
            naming.Count.ShouldBe(2, "the count and the window");
            naming.ShouldNotContain(t => t.Contains("max(", StringComparison.OrdinalIgnoreCase), "the token is never narrowed");
        }
        else
        {
            naming.ShouldBeEmpty();
        }
    }

    // The per-request cost stays nine with a held ad: the narrowing adds no statement.
    [Fact]
    public async Task A_held_ad_costs_no_statement()
    {
        var (client, userId) = await RegisterUserAsync(factory, "held-cost", Ct);
        await GiveConsentAsync(client, true);
        var watch = await SeedWatchAsync(factory, userId, Ct);
        var ad = await SeedAdAsync(factory, "J", Ct);
        await SeedMatchAsync(factory, userId, ad, Base.AddMinutes(1), Ct);
        await SeedHitAsync(factory, userId, ad, watch, Base.AddMinutes(10), Ct);

        using var scope = factory.Services.CreateScope();
        var counter = new StatementCounter();
        await using var db = NewContext(scope, counter);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);

        var feed = await new GetMyNotificationFeedQueryHandler(
                db, currentUser, Substitute.For<IMatchProfileBuilder>(), Substitute.For<IPerUserJobAdSearchQuery>())
            .Handle(new GetMyNotificationFeedQuery(), Ct);

        feed.Items.Single().JobAdId.ShouldBe(ad.Value);
        counter.Count.ShouldBe(9);
    }

    // security V6, declared unreachable: no request carries a signed-in user without a JobSeeker row (registration makes
    // both; over HTTP such a user is a 401), so this asserts only that the row commands degrade safely if that ever
    // breaks: Success, never a NotFound. On Npgsql because a row command reaches the match source's ExecuteUpdate.
    [Fact]
    public async Task The_row_commands_answer_success_for_a_signed_in_user_without_a_job_seeker_row()
    {
        using var scope = factory.Services.CreateScope();
        await using var db = NewContext(scope);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());

        (await new MarkNotificationReadCommandHandler(db, currentUser).Handle(new MarkNotificationReadCommand(Guid.NewGuid()), Ct))
            .IsSuccess.ShouldBeTrue();
        (await new DismissNotificationCommandHandler(db, currentUser).Handle(new DismissNotificationCommand(Guid.NewGuid()), Ct))
            .IsSuccess.ShouldBeTrue();
    }

    // ── seeding ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Seven read hits (newest) fill a window of six, a held ad with an unread hit sits behind them, and an unread ad
    /// behind that: only a fallback can name the unread ad, and it must never name the held one.
    /// </summary>
    private async Task<(Guid UserId, JobAdId Held, JobAdId Unread)> SeedCrowdedWindowAsync(string prefix, bool onlyMatched)
    {
        var (client, userId) = await RegisterUserAsync(factory, prefix, Ct);
        await GiveConsentAsync(client, true);
        var watch = await SeedWatchAsync(factory, userId, onlyMatched, Ct);
        for (var n = 1; n <= 7; n++)
        {
            var crowder = await SeedAdAsync(factory, $"Läst {n}", Ct);
            await SeedHitAsync(factory, userId, crowder, watch, Base.AddMinutes(60 + n), Ct);
            await ReadHitsInBellAsync(factory, userId, crowder, Ct);
        }

        var held = await SeedAdAsync(factory, "Hållen", Ct);
        await SeedMatchAsync(factory, userId, held, Base.AddMinutes(40), Ct);
        await SeedHitAsync(factory, userId, held, watch, Base.AddMinutes(40), Ct);
        var unread = await SeedAdAsync(factory, "Oläst", Ct);
        await SeedHitAsync(factory, userId, unread, watch, Base.AddMinutes(20), Ct);
        return (userId, held, unread);
    }

    private static async Task GiveConsentAsync(HttpClient client, bool enabled) =>
        (await client.PutAsJsonAsync("/api/v1/me/background-match-notification-consent", new { enabled }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

    // A profile with an occupation, so an "endast matchade" watch's candidates go to the grader below.
    private static IMatchProfileBuilder OccupationProfile()
    {
        var profileBuilder = Substitute.For<IMatchProfileBuilder>();
        profileBuilder.BuildFullForSortAsync(Arg.Any<CancellationToken>())
            .Returns(new FullCandidateMatchProfile(new CandidateMatchProfile("", ["ssyk-2512"], [], [], []), []));
        return profileBuilder;
    }

    // The grader, stubbed as in NotificationFeedReadStateEndpointTests: every candidate it is sent grades at least Good.
    private static IPerUserJobAdSearchQuery GradeEverythingAsMatching()
    {
        var search = Substitute.For<IPerUserJobAdSearchQuery>();
        search.FilterToMatchingAsync(
                Arg.Any<FullCandidateMatchProfile>(), Arg.Any<IReadOnlyCollection<JobAdId>>(), Arg.Any<CancellationToken>())
            .Returns(call => (IReadOnlySet<JobAdId>)call.ArgAt<IReadOnlyCollection<JobAdId>>(1).ToHashSet());
        return search;
    }
}
