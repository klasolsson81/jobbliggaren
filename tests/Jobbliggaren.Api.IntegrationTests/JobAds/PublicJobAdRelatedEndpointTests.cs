using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// The related ads under a public job page, <c>GET /api/v1/public/job-ads/{id}/related</c> (ADR 0182), on the real host
/// and real Postgres with no Authorization header. Premises come from production paths: imported ads through
/// <c>JobAd.Import</c>, lifecycle changes through <c>Archive</c> and <c>Erase</c>, the never-imported ad through
/// <c>POST /api/v1/job-ads</c> itself. This collection's table is shared and never truncated, so every test gives its
/// ads an occupation group of its own.
/// </summary>
[Collection("Api")]
public sealed class PublicJobAdRelatedEndpointTests(ApiFactory factory)
{
    private const string Route = "/api/v1/public/job-ads";

    private static readonly string[] RowMembers =
        ["companyName", "id", "municipalityName", "publishedAt", "regionName", "title"];

    private readonly HttpClient _anonymous = factory.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string OwnGroup() => $"grp_related_{Guid.NewGuid():N}";

    private async Task<Guid> ImportAsync(
        string group, string employer, int publishedHoursAgo, int? expiresInDays = 30,
        Func<JobAd, IDateTimeProvider, Result>? then = null,
        string municipality = "AvNB_uwa_6n6", string region = "CifL_Rzy_Mku")
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var externalId = TestIds.ExternalId();

        var jobAd = JobAd.Import(
            title: $"Systemutvecklare hos {employer}",
            company: Company.Create(employer).Value,
            description: "Vi söker en systemutvecklare till vårt team.",
            url: $"https://arbetsformedlingen.se/platsbanken/annonser/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: $"{{\"id\":\"{externalId}\"}}",
            facets: TestFacets.From(
                occupationGroup: group, municipality: municipality, region: region, organizationNumber: "5560000000"),
            declaredContacts: [],
            publishedAt: clock.UtcNow.AddHours(-publishedHoursAgo),
            expiresAt: expiresInDays is { } days ? clock.UtcNow.AddDays(days) : null,
            clock: clock,
            applicationUrl: null, extractTerms: TestKeywordExtraction.None).Value;

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(Ct);

        // A lifecycle change goes through the aggregate's own transition, never a column write (#843).
        if (then is not null)
        {
            then(jobAd, clock).IsSuccess.ShouldBeTrue();
            await db.SaveChangesAsync(Ct);
        }

        return jobAd.Id.Value;
    }

    private Task<HttpResponseMessage> RelatedAsync(Guid id, string query = "") =>
        _anonymous.GetAsync($"{Route}/{id}/related{query}", Ct);

    /// <summary>
    /// All a reader receives except <c>Date</c>, and the body with its <c>traceId</c> removed: the status, every other
    /// header and every other member.
    /// </summary>
    private static async Task<(HttpStatusCode Status, string Headers, string Body)> WireOf(HttpResponseMessage response)
    {
        var headers = response.Headers.Concat(response.Content.Headers)
            .Where(h => !string.Equals(h.Key, "Date", StringComparison.OrdinalIgnoreCase))
            .Where(h => !string.Equals(h.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
            .Select(h => $"{h.Key}: {string.Join(",", h.Value)}")
            .Order(StringComparer.Ordinal);
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!.AsObject();
        body.Remove("traceId");
        return (response.StatusCode, string.Join('\n', headers), body.ToJsonString());
    }

    [Fact]
    public async Task A_listed_anchor_answers_200_with_one_items_member_of_six_member_rows()
    {
        var group = OwnGroup();
        var anchor = await ImportAsync(group, "Ankarbolaget AB", 1);
        var related = await ImportAsync(group, "Spotify AB", 2);

        var response = await RelatedAsync(anchor);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
        var raw = await response.Content.ReadAsStringAsync(Ct);
        var body = JsonDocument.Parse(raw).RootElement;
        body.EnumerateObject().Select(p => p.Name).ToArray().ShouldBe(["items"],
            "an envelope of one member, so the answer can grow without breaking a client");
        var row = body.GetProperty("items").EnumerateArray().ShouldHaveSingleItem();
        row.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray().ShouldBe(
            RowMembers, "a related row is the catalogue's six-member row and nothing else (ADR 0166 D10 trigger 15)");
        row.GetProperty("id").GetString().ShouldBe(related.ToString("D"), "the id is lower case on the wire");
        row.GetProperty("companyName").GetString().ShouldBe("Spotify AB");
        row.GetProperty("municipalityName").GetString().ShouldBe("Stockholm");
        row.GetProperty("regionName").GetString().ShouldBe("Stockholms län");
        raw.ShouldNotContain("5560000000", Case.Insensitive, "the org.nr never leaves the handler");
    }

    [Fact]
    public async Task A_listed_anchor_whose_group_has_no_other_employer_answers_an_empty_list()
    {
        var group = OwnGroup();
        var anchor = await ImportAsync(group, "Ankarbolaget AB", 1);
        await ImportAsync(group, "Ankarbolaget AB", 2);

        var response = await RelatedAsync(anchor);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldBe("""{"items":[]}""");
    }

    [Fact]
    public async Task Every_anchor_that_is_not_listed_answers_the_identical_uncacheable_404()
    {
        var group = OwnGroup();
        await ImportAsync(group, "Spotify AB", 3);
        var archived = await ImportAsync(group, "Ankarbolaget AB", 1, then: (ad, clock) => ad.Archive(clock));
        // Still Active: only the clock has passed its deadline, before ExpireJobAdsJob runs.
        var pastDeadline = await ImportAsync(group, "Ankarbolaget AB", 48, expiresInDays: -1);
        var erased = await ImportAsync(group, "Ankarbolaget AB", 1, then: (ad, clock) => ad.Erase(clock));
        using var author = factory.CreateClient();
        author.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await AuthTestHelpers.RegisterAndGetSessionIdAsync(factory, ct: Ct));
        var created = await author.PostAsJsonAsync("/api/v1/job-ads", new
        {
            title = "Systemutvecklare",
            companyName = "Ankarbolaget AB",
            description = "Text som någon skrev själv.",
            url = "https://example.com/jobb/1",
            source = "Platsbanken",
            publishedAt = DateTimeOffset.UtcNow.AddDays(-1),
            expiresAt = (DateTimeOffset?)null,
        }, Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var neverImported = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();

        var answers = new[]
        {
            await WireOf(await RelatedAsync(Guid.NewGuid())),
            await WireOf(await RelatedAsync(neverImported)),
            await WireOf(await RelatedAsync(archived)),
            await WireOf(await RelatedAsync(pastDeadline)),
            await WireOf(await RelatedAsync(erased)),
        };

        answers[0].Status.ShouldBe(HttpStatusCode.NotFound);
        answers[0].Headers.ShouldContain("Cache-Control: no-store");
        JsonDocument.Parse(answers[0].Body).RootElement.GetProperty("title").GetString().ShouldBe("JobAd.NotFound");
        answers.Distinct().ShouldHaveSingleItem(
            "no state of a non-listed anchor can be told apart here: the detail already tells 404 from 410, and this route "
            + "tells strictly less.");
    }

    [Fact]
    public async Task A_caller_cannot_choose_the_group_the_place_or_the_order()
    {
        var group = OwnGroup();
        var anchor = await ImportAsync(group, "Ankarbolaget AB", 1);
        await ImportAsync(group, "Spotify AB", 2);
        await ImportAsync(OwnGroup(), "Klarna AB", 1);

        var plain = await (await RelatedAsync(anchor)).Content.ReadAsStringAsync(Ct);
        var asked = await (await RelatedAsync(
                anchor, $"?group={OwnGroup()}&occupationGroup=x&kommun=0180&municipality=0180&lan=01&region=01&sort=asc&take=50"))
            .Content.ReadAsStringAsync(Ct);

        asked.ShouldBe(plain, "every selection key comes from the anchor's row (ADR 0166 D10 trigger 17)");
    }

    [Fact]
    public async Task An_empty_id_is_a_400_that_is_not_cached()
    {
        var response = await RelatedAsync(Guid.Empty);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
    }
}
