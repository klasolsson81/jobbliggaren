using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.IntegrationTests.Sessions;
using Jobbliggaren.Application.CompanyWatches.Queries;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.CompanyWatches;

/// <summary>
/// ADR 0175 D7 (#1663 PR 2) — end-to-end over the Branschbevakning count (<c>/new-ad-counts</c>), the page of one
/// criterion's new ads (<c>/{id}/new-ads</c>) and the write that acknowledges it (<c>/{id}/new-ads/seen</c>), on real
/// Postgres: the one set definition behind both reads, the closed states, the window token, the monotone seen write
/// and the owner scoping.
///
/// <para>
/// <b>Every state is one production writes.</b> Criteria come from <see cref="CompanyWatchCriterion.Create"/> and
/// <see cref="CompanyWatchCriterion.UpdateCriteria"/>, their scan state from <see cref="CompanyWatchCriterion.RecordScan"/>,
/// hits from <see cref="CompanyWatchCriterionAdHit.Create"/> — the calls the nightly scan makes — and ads from
/// <see cref="JobAd.Import"/>, ingested before the hit is stamped as the scan's window guarantees, and archived
/// through <see cref="JobAd.Archive"/>. The shared Api database is never reset, so every test uses fresh users.
/// </para>
/// </summary>
[Collection("Api")]
public class CriterionNewAdsEndpointTests(ApiFactory factory)
{
    private const string Endpoint = "/api/v1/me/company-watch-criteria";
    // Postgres keeps microseconds, so a tick-precise instant would not survive the round trip the exact comparisons
    // below make.
    private static readonly DateTimeOffset Created = AtMicrosecondPrecision(DateTimeOffset.UtcNow.AddDays(-3));
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateTimeOffset AtMicrosecondPrecision(DateTimeOffset t) =>
        t.AddTicks(-(t.Ticks % TimeSpan.TicksPerMicrosecond));

    [Fact]
    public async Task The_three_routes_require_authentication()
    {
        var anonymous = factory.CreateClient();
        var id = Guid.NewGuid();

        (await anonymous.GetAsync($"{Endpoint}/new-ad-counts", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync($"{Endpoint}/{id}/new-ads", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync($"{Endpoint}/{id}/new-ads/seen", new { seenThrough = DateTimeOffset.UtcNow }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_count_is_null_until_the_criterion_is_scanned_and_a_real_zero_after()
    {
        var user = await RegisterAsync();
        var unscanned = await SeedCriterionAsync(user.UserId);
        var scannedEmpty = await SeedCriterionAsync(user.UserId, CompanyWatchCriterionScanOutcome.Scanned);
        var tooBroad = await SeedCriterionAsync(user.UserId, CompanyWatchCriterionScanOutcome.TooBroad);
        var notMaterialised = await SeedCriterionAsync(user.UserId, CompanyWatchCriterionScanOutcome.NotMaterialised);

        var counts = await CountsAsync(user.Client);

        counts[unscanned.Value].ShouldBeNull();
        counts[scannedEmpty.Value].ShouldBe(0);
        counts[tooBroad.Value].ShouldBeNull();
        counts[notMaterialised.Value].ShouldBeNull();
    }

    [Fact]
    public async Task The_page_lists_exactly_the_ads_the_count_counted_newest_first_and_leaves_out_an_ad_that_left_Active()
    {
        var user = await RegisterAsync();
        var criterion = await SeedCriterionAsync(user.UserId, CompanyWatchCriterionScanOutcome.Scanned);
        var older = await SeedHitAsync(criterion, Created.AddHours(1));
        var newer = await SeedHitAsync(criterion, Created.AddHours(2));
        var archived = await SeedHitAsync(criterion, Created.AddHours(3));
        await ArchiveAsync(archived);

        (await CountsAsync(user.Client))[criterion.Value].ShouldBe(2);
        var page = await PageAsync(user.Client, criterion);

        page.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("ad").GetProperty("id").GetGuid())
            .ShouldBe([newer.Value, older.Value]);
        page.GetProperty("tooBroad").GetBoolean().ShouldBeFalse();
        page.GetProperty("notMaterialised").GetBoolean().ShouldBeFalse();
        page.GetProperty("criterion").GetProperty("id").GetGuid().ShouldBe(criterion.Value);
    }

    [Fact]
    public async Task The_page_names_a_refused_or_unscanned_criterion_and_offers_nothing_to_acknowledge()
    {
        var user = await RegisterAsync();
        var tooBroad = await SeedCriterionAsync(user.UserId, CompanyWatchCriterionScanOutcome.TooBroad);
        var unscanned = await SeedCriterionAsync(user.UserId);

        var refused = await PageAsync(user.Client, tooBroad);
        var notYet = await PageAsync(user.Client, unscanned);

        refused.GetProperty("tooBroad").GetBoolean().ShouldBeTrue();
        refused.GetProperty("rows").GetArrayLength().ShouldBe(0);
        refused.GetProperty("acknowledgedThrough").ValueKind.ShouldBe(JsonValueKind.Null);
        notYet.GetProperty("notMaterialised").GetBoolean().ShouldBeTrue();
        notYet.GetProperty("acknowledgedThrough").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task The_window_token_is_the_newest_hit_shown_and_posting_it_resets_the_count_without_swallowing_a_later_hit()
    {
        var user = await RegisterAsync();
        var criterion = await SeedCriterionAsync(user.UserId, CompanyWatchCriterionScanOutcome.Scanned);
        await SeedHitAsync(criterion, Created.AddHours(1));
        await SeedHitAsync(criterion, Created.AddHours(2));

        var token = (await PageAsync(user.Client, criterion)).GetProperty("acknowledgedThrough").GetString()!;
        DateTimeOffset.Parse(token, System.Globalization.CultureInfo.InvariantCulture)
            .ShouldBe(Created.AddHours(2), TimeSpan.FromMilliseconds(1));

        // A hit written after the page was read must survive the acknowledgement of that page.
        await SeedHitAsync(criterion, Created.AddHours(5));
        (await PostSeenAsync(user.Client, criterion, token)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await CountsAsync(user.Client))[criterion.Value].ShouldBe(1);
    }

    [Fact]
    public async Task The_seen_write_never_moves_backward()
    {
        var user = await RegisterAsync();
        var criterion = await SeedCriterionAsync(user.UserId, CompanyWatchCriterionScanOutcome.Scanned);
        await SeedHitAsync(criterion, Created.AddHours(1));
        await SeedHitAsync(criterion, Created.AddHours(2));
        var updatedAt = await UpdatedAtAsync(criterion);

        (await PostSeenAsync(user.Client, criterion, Created.AddHours(2).ToString("O"))).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);
        (await PostSeenAsync(user.Client, criterion, Created.AddHours(1).ToString("O"))).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);

        (await SeenThroughAsync(criterion)).ShouldBe(Created.AddHours(2));
        (await UpdatedAtAsync(criterion)).ShouldBe(updatedAt);
        (await CountsAsync(user.Client))[criterion.Value].ShouldBe(0);
    }

    [Fact]
    public async Task The_seen_write_takes_a_token_in_another_offset_as_the_same_instant()
    {
        var user = await RegisterAsync();
        var criterion = await SeedCriterionAsync(user.UserId, CompanyWatchCriterionScanOutcome.Scanned);
        await SeedHitAsync(criterion, Created.AddHours(1));
        var stockholm = Created.AddHours(1).ToOffset(TimeSpan.FromHours(2)).ToString("O");

        (await PostSeenAsync(user.Client, criterion, stockholm)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await SeenThroughAsync(criterion)).ShouldBe(Created.AddHours(1));
    }

    [Fact]
    public async Task The_seen_write_without_a_token_is_a_400_and_writes_nothing()
    {
        var user = await RegisterAsync();
        var criterion = await SeedCriterionAsync(user.UserId, CompanyWatchCriterionScanOutcome.Scanned);

        var response = await user.Client.PostAsJsonAsync($"{Endpoint}/{criterion.Value}/new-ads/seen", new { }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await SeenThroughAsync(criterion)).ShouldBeNull();
    }

    [Fact]
    public async Task A_foreign_criterion_is_the_same_404_as_an_unknown_one_on_the_page_and_the_seen_write()
    {
        var owner = await RegisterAsync();
        var stranger = await RegisterAsync();
        var criterion = await SeedCriterionAsync(owner.UserId, CompanyWatchCriterionScanOutcome.Scanned);
        await SeedHitAsync(criterion, Created.AddHours(1));

        (await stranger.Client.GetAsync($"{Endpoint}/{criterion.Value}/new-ads", Ct)).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        (await stranger.Client.GetAsync($"{Endpoint}/{Guid.NewGuid()}/new-ads", Ct)).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
        (await PostSeenAsync(stranger.Client, criterion, Created.AddHours(1).ToString("O"))).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);

        (await SeenThroughAsync(criterion)).ShouldBeNull();
        (await CountsAsync(stranger.Client)).ShouldNotContainKey(criterion.Value);
        (await CountsAsync(owner.Client))[criterion.Value].ShouldBe(1);
    }

    [Fact]
    public async Task An_edit_hides_the_hits_of_the_membership_it_replaced()
    {
        var user = await RegisterAsync();
        var criterion = await SeedCriterionAsync(user.UserId, CompanyWatchCriterionScanOutcome.Scanned);
        await SeedHitAsync(criterion, Created.AddHours(1));
        (await CountsAsync(user.Client))[criterion.Value].ShouldBe(1);

        await EditCriterionAsync(criterion, Created.AddHours(2), ["63910", "63920"]);

        (await CountsAsync(user.Client))[criterion.Value].ShouldBeNull();
        var beforeTheNextScan = await PageAsync(user.Client, criterion);
        beforeTheNextScan.GetProperty("notMaterialised").GetBoolean().ShouldBeTrue();
        beforeTheNextScan.GetProperty("tooBroad").GetBoolean().ShouldBeFalse();
        beforeTheNextScan.GetProperty("rows").GetArrayLength().ShouldBe(0);
        beforeTheNextScan.GetProperty("acknowledgedThrough").ValueKind.ShouldBe(JsonValueKind.Null);

        await RecordScanAsync(criterion, CompanyWatchCriterionScanOutcome.Scanned, Created.AddHours(3));

        (await CountsAsync(user.Client))[criterion.Value].ShouldBe(0);
        var afterTheNextScan = await PageAsync(user.Client, criterion);
        afterTheNextScan.GetProperty("notMaterialised").GetBoolean().ShouldBeFalse();
        afterTheNextScan.GetProperty("rows").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task A_refused_criterion_narrowed_by_an_edit_is_not_scanned_rather_than_refused()
    {
        var user = await RegisterAsync();
        var criterion = await SeedCriterionAsync(
            user.UserId, CompanyWatchCriterionScanOutcome.TooBroad, sniCodes: ["63910", "63920"]);

        await EditCriterionAsync(criterion, Created.AddHours(2), ["63910"]);

        (await CountsAsync(user.Client))[criterion.Value].ShouldBeNull();
        var page = await PageAsync(user.Client, criterion);
        page.GetProperty("tooBroad").GetBoolean().ShouldBeFalse();
        page.GetProperty("notMaterialised").GetBoolean().ShouldBeTrue();
        page.GetProperty("rows").GetArrayLength().ShouldBe(0);
        page.GetProperty("acknowledgedThrough").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Both_reads_are_never_cached_and_the_mark_is_not_assessed_without_a_stated_occupation()
    {
        var user = await RegisterAsync();
        var criterion = await SeedCriterionAsync(user.UserId, CompanyWatchCriterionScanOutcome.Scanned);
        await SeedHitAsync(criterion, Created.AddHours(1));

        var counts = await user.Client.GetAsync($"{Endpoint}/new-ad-counts", Ct);
        var page = await user.Client.GetAsync($"{Endpoint}/{criterion.Value}/new-ads", Ct);

        counts.Headers.CacheControl!.NoStore.ShouldBeTrue();
        counts.Headers.CacheControl.Private.ShouldBeTrue();
        page.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var body = await page.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("matchingAssessed").GetBoolean().ShouldBeFalse();
        body.GetProperty("rows")[0].GetProperty("matchesYou").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task The_seen_write_never_reaches_past_now()
    {
        // The actor is a client that sends a later instant than any window it was shown. Stored as sent, it would
        // hide every hit the scan writes until that instant.
        var user = await RegisterAsync();
        var criterion = await SeedCriterionAsync(user.UserId, CompanyWatchCriterionScanOutcome.Scanned);
        var before = AtMicrosecondPrecision(DateTimeOffset.UtcNow);

        (await PostSeenAsync(user.Client, criterion, DateTimeOffset.UtcNow.AddDays(30).ToString("O"))).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);

        var seen = (await SeenThroughAsync(criterion)).ShouldNotBeNull();
        seen.ShouldBeGreaterThanOrEqualTo(before);
        seen.ShouldBeLessThanOrEqualTo(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task The_set_itself_holds_no_hit_of_a_criterion_whose_last_scan_was_refused()
    {
        // Both handlers check the outcome themselves, so only a direct read pins that the set does: ADR 0175 D6 makes
        // it the one definition every surface reads. The state is the scan's: a criterion scanned with a hit, then
        // refused on a later night when its membership grew past the gate, with no edit between.
        var user = await RegisterAsync();
        var criterion = await SeedCriterionAsync(user.UserId, CompanyWatchCriterionScanOutcome.Scanned);
        await SeedHitAsync(criterion, Created.AddHours(1));
        await RecordScanAsync(criterion, CompanyWatchCriterionScanOutcome.TooBroad, Created.AddHours(2));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await NewCriterionAdSet.NewHits(db, user.UserId).CountAsync(Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task The_set_itself_holds_no_hit_of_a_scan_run_in_flight_across_an_edit()
    {
        var user = await RegisterAsync();
        var criterion = await SeedCriterionAsync(user.UserId);
        var until = Created.AddHours(1);
        await EditCriterionAsync(criterion, until.AddMinutes(1), ["63910", "63920"]);
        var ad = await SeedHitAsync(criterion, until.AddMinutes(2));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var scanned = await db.CompanyWatchCriteria.SingleAsync(c => c.Id == criterion, Ct);
        scanned.RecordScan(
            CompanyWatchCriterionScanOutcome.Scanned, until, new FakeDateTimeProvider(until.AddMinutes(3)));
        await db.SaveChangesAsync(Ct);

        (await NewCriterionAdSet.NewHits(db, user.UserId).AnyAsync(h => h.JobAdId == ad, Ct)).ShouldBeFalse();
    }

    // ─────────────────────────── Seeding through the production writers

    private sealed record User(HttpClient Client, Guid UserId);

    private async Task<User> RegisterAsync()
    {
        var client = factory.CreateClient();
        var email = $"criterion-new-ads-{Guid.NewGuid()}@example.com";
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(factory, email, ct: Ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

        using var scope = factory.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email);
        user.ShouldNotBeNull();
        return new User(client, user.Id);
    }

    // A criterion created three days back; with an outcome, the nightly scan has recorded it.
    private async Task<CompanyWatchCriterionId> SeedCriterionAsync(
        Guid userId, CompanyWatchCriterionScanOutcome? outcome = null, string[]? sniCodes = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var criterion = CompanyWatchCriterion.Create(
            userId, CompanyWatchCriteriaSpec.Create(sniCodes ?? ["63910"], ["0180"]).Value, null,
            new FakeDateTimeProvider(Created)).Value;
        if (outcome == CompanyWatchCriterionScanOutcome.NotMaterialised)
        {
            // The scan's read-age guard answers NotMaterialised for a criterion it has scanned before, once its
            // materialisation is stale (CompanyWatchBrowseQuery); alone it would leave ScannedThrough null.
            criterion.RecordScan(CompanyWatchCriterionScanOutcome.Scanned, Created.AddMinutes(30),
                new FakeDateTimeProvider(Created.AddMinutes(30)));
            criterion.RecordScan(CompanyWatchCriterionScanOutcome.NotMaterialised, Created.AddMinutes(60),
                new FakeDateTimeProvider(Created.AddMinutes(60)));
        }
        else if (outcome is { } o)
            criterion.RecordScan(o, Created.AddMinutes(30), new FakeDateTimeProvider(Created.AddMinutes(30)));
        db.CompanyWatchCriteria.Add(criterion);
        await db.SaveChangesAsync(Ct);
        return criterion.Id;
    }

    private async Task EditCriterionAsync(CompanyWatchCriterionId id, DateTimeOffset at, string[] sniCodes)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var criterion = await db.CompanyWatchCriteria.SingleAsync(c => c.Id == id, Ct);
        criterion.UpdateCriteria(CompanyWatchCriteriaSpec.Create(sniCodes, ["0180"]).Value,
            new FakeDateTimeProvider(at)).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(Ct);
    }

    // An Active ad ingested five minutes before the scan stamps its hit, the order the scan's window guarantees.
    private async Task<JobAdId> SeedHitAsync(CompanyWatchCriterionId criterionId, DateTimeOffset hitAt)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ingested = new FakeDateTimeProvider(hitAt.AddMinutes(-5));
        var externalId = $"ext-{Guid.NewGuid():N}";
        var payload = $"{{\"id\":\"{externalId}\"}}";
        var ad = JobAd.Import(
            title: "Roll",
            company: Company.Create("Bolag AB").Value,
            description: "beskrivning",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: payload,
            facets: TestFacets.FromPayload(payload),
            publishedAt: ingested.UtcNow,
            expiresAt: ingested.UtcNow.AddDays(60),
            clock: ingested, declaredContacts: [], applicationUrl: null, extractTerms: TestKeywordExtraction.None).Value;
        db.JobAds.Add(ad);
        var criterion = await db.CompanyWatchCriteria.AsNoTracking().SingleAsync(c => c.Id == criterionId, Ct);
        db.CompanyWatchCriterionAdHits.Add(
            CompanyWatchCriterionAdHit.Create(criterion, ad.Id, new FakeDateTimeProvider(hitAt)).Value);
        await db.SaveChangesAsync(Ct);
        return ad.Id;
    }

    private async Task ArchiveAsync(JobAdId id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ad = await db.JobAds.SingleAsync(j => j.Id == id, Ct);
        ad.Archive(scope.ServiceProvider.GetRequiredService<IDateTimeProvider>()).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(Ct);
    }

    private async Task RecordScanAsync(
        CompanyWatchCriterionId id, CompanyWatchCriterionScanOutcome outcome, DateTimeOffset at)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var criterion = await db.CompanyWatchCriteria.SingleAsync(c => c.Id == id, Ct);
        criterion.RecordScan(outcome, at, new FakeDateTimeProvider(at));
        await db.SaveChangesAsync(Ct);
    }

    // ─────────────────────────── Reading back

    private static async Task<Dictionary<Guid, int?>> CountsAsync(HttpClient client)
    {
        var response = await client.GetAsync($"{Endpoint}/new-ad-counts", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return body.GetProperty("items").EnumerateArray().ToDictionary(
            item => item.GetProperty("criterionId").GetGuid(),
            item => item.GetProperty("newAds").ValueKind == JsonValueKind.Null
                ? (int?)null
                : item.GetProperty("newAds").GetInt32());
    }

    private static async Task<JsonElement> PageAsync(HttpClient client, CompanyWatchCriterionId id)
    {
        var response = await client.GetAsync($"{Endpoint}/{id.Value}/new-ads", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static Task<HttpResponseMessage> PostSeenAsync(HttpClient client, CompanyWatchCriterionId id, string token) =>
        client.PostAsync(
            $"{Endpoint}/{id.Value}/new-ads/seen",
            new StringContent($"{{\"seenThrough\":\"{token}\"}}", System.Text.Encoding.UTF8, "application/json"),
            Ct);

    private async Task<DateTimeOffset?> SeenThroughAsync(CompanyWatchCriterionId id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.CompanyWatchCriteria.AsNoTracking().SingleAsync(c => c.Id == id, Ct)).NewAdsSeenThrough;
    }

    private async Task<DateTimeOffset> UpdatedAtAsync(CompanyWatchCriterionId id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.CompanyWatchCriteria.AsNoTracking().SingleAsync(c => c.Id == id, Ct)).UpdatedAt;
    }
}
