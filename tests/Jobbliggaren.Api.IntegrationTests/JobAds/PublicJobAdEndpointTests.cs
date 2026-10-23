using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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
/// The anonymous job-ad detail <c>GET /api/v1/public/job-ads/{id}</c> (ADR 0160) on the real host
/// and real Postgres, with no Authorization header. Premises come from production paths: imported
/// ads through <c>JobAd.Import</c>, lifecycle changes through <c>Archive</c>/<c>Erase</c>, and the
/// never-imported ad through <c>POST /api/v1/job-ads</c> itself.
/// </summary>
[Collection("Api")]
public sealed class PublicJobAdEndpointTests(ApiFactory factory)
{
    private const string Route = "/api/v1/public/job-ads";

    private static readonly string[] WireMembers =
    [
        "companyName", "description", "employmentTypeConceptId", "expiresAt", "id",
        "municipalityConceptId", "municipalityName", "occupationGroupConceptId", "occupationGroupName",
        "publishedAt", "regionConceptId", "regionName", "title", "url", "worktimeExtentConceptId",
    ];

    private readonly HttpClient _anonymous = factory.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Guid> ImportAsync(
        int publishedDaysAgo, int? expiresInDays, Func<JobAd, IDateTimeProvider, Result>? then = null,
        string municipality = "mun_public", string region = "reg_public", string occupationGroup = "grp_public")
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var externalId = TestIds.ExternalId();
        var payload = $"{{\"id\":\"{externalId}\"}}";

        var jobAd = JobAd.Import(
            title: "Backend-utvecklare",
            company: Company.Create("Exempelbolaget AB").Value,
            description: "Vi söker en backend-utvecklare till vårt team.",
            url: $"https://arbetsformedlingen.se/platsbanken/annonser/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: payload,
            facets: TestFacets.From(
                occupationGroup: occupationGroup, municipality: municipality, region: region,
                employmentType: "et_public", worktimeExtent: "wt_public", organizationNumber: "5560000000"),
            declaredContacts: [],
            publishedAt: clock.UtcNow.AddDays(-publishedDaysAgo),
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

    private Task<HttpResponseMessage> GetAsync(Guid id) => _anonymous.GetAsync($"{Route}/{id}", Ct);

    /// <summary>All a reader receives except <c>Date</c>: the status, every other header and the raw body.</summary>
    private static async Task<(HttpStatusCode Status, string Headers, string Body)> WireOf(HttpResponseMessage response)
    {
        var headers = response.Headers.Concat(response.Content.Headers)
            .Where(h => !string.Equals(h.Key, "Date", StringComparison.OrdinalIgnoreCase))
            .Select(h => $"{h.Key}: {string.Join(",", h.Value)}")
            .Order(StringComparer.Ordinal);
        return (response.StatusCode, string.Join('\n', headers), await response.Content.ReadAsStringAsync(Ct));
    }

    private static async Task<(HttpStatusCode Status, string? Title, string? Detail, string Cache)> ProblemOf(
        HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return (response.StatusCode,
            problem.GetProperty("title").GetString(),
            problem.GetProperty("detail").GetString(),
            response.Headers.CacheControl?.ToString() ?? "");
    }

    [Fact]
    public async Task A_listed_ad_answers_200_with_exactly_the_public_members()
    {
        var id = await ImportAsync(publishedDaysAgo: 1, expiresInDays: 30);

        var response = await GetAsync(id);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray().ShouldBe(
            WireMembers, "the wire carries the pinned DTO and nothing else: no org.nr, no contacts, no status.");
        body.GetProperty("id").GetGuid().ShouldBe(id);
        body.GetProperty("companyName").GetString().ShouldBe("Exempelbolaget AB");
        body.GetProperty("municipalityConceptId").GetString().ShouldBe("mun_public");
        body.GetProperty("employmentTypeConceptId").GetString().ShouldBe("et_public");
        // Ids the snapshot does not know: the name members are present and null, never dropped (ADR 0171).
        body.GetProperty("municipalityName").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("regionName").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("occupationGroupName").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_listed_ad_with_snapshot_concept_ids_carries_the_kommun_lan_and_group_names_on_the_wire()
    {
        // Real ids from the seeded taxonomy snapshot (TaxonomySnapshotSeeder runs in the test host):
        // Stockholm kommun under Stockholms län, and an occupation group.
        var id = await ImportAsync(publishedDaysAgo: 1, expiresInDays: 30,
            municipality: "AvNB_uwa_6n6", region: "CifL_Rzy_Mku", occupationGroup: "DJh5_yyF_hEM");

        var response = await GetAsync(id);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("municipalityName").GetString().ShouldBe("Stockholm");
        body.GetProperty("regionName").GetString().ShouldBe("Stockholms län");
        body.GetProperty("occupationGroupName").GetString().ShouldBe("Mjukvaru- och systemutvecklare m.fl.");
        body.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray().ShouldBe(WireMembers);
    }

    [Fact]
    public async Task Archived_past_deadline_and_erased_ads_answer_the_identical_410()
    {
        var archived = await ImportAsync(2, 30, (ad, clock) => ad.Archive(clock));
        // Still Active: only the clock has passed its deadline, before ExpireJobAdsJob runs.
        var pastDeadline = await ImportAsync(publishedDaysAgo: 30, expiresInDays: -1);
        var erased = await ImportAsync(2, 30, (ad, clock) => ad.Erase(clock));

        var answers = new[]
        {
            await WireOf(await GetAsync(archived)),
            await WireOf(await GetAsync(pastDeadline)),
            await WireOf(await GetAsync(erased)),
        };

        answers[0].Status.ShouldBe(HttpStatusCode.Gone);
        var problem = JsonDocument.Parse(answers[0].Body).RootElement;
        problem.GetProperty("title").GetString().ShouldBe("JobAd.Gone");
        problem.GetProperty("detail").GetString().ShouldBe("Annonsen är inte längre tillgänglig.");
        answers[0].Headers.ShouldContain("Cache-Control: no-store");
        answers.Distinct().ShouldHaveSingleItem(
            "an ended ad must not be told apart from an erased one: erasure follows an Art. 17 request, "
            + "so a different answer would publish which ads someone asked to have erased.");
    }

    [Fact]
    public async Task An_ad_created_through_the_api_answers_like_an_unknown_id()
    {
        // POST /api/v1/job-ads lets a signed-in user claim Source=Platsbanken; the row has no
        // External, so it was never imported and is never public.
        using var author = factory.CreateClient();
        author.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await AuthTestHelpers.RegisterAndGetSessionIdAsync(factory, ct: Ct));
        var created = await author.PostAsJsonAsync("/api/v1/job-ads", new
        {
            title = "Backend-utvecklare",
            companyName = "Exempelbolaget AB",
            description = "Text som någon skrev själv.",
            url = "https://example.com/jobb/1",
            source = "Platsbanken",
            publishedAt = DateTimeOffset.UtcNow.AddDays(-1),
            expiresAt = (DateTimeOffset?)null,
        }, Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var manualId = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();

        var manual = await WireOf(await GetAsync(manualId));
        var unknown = await WireOf(await GetAsync(Guid.NewGuid()));

        unknown.Status.ShouldBe(HttpStatusCode.NotFound);
        unknown.Headers.ShouldContain("Cache-Control: no-store");
        manual.ShouldBe(unknown);
    }

    [Fact]
    public async Task The_public_and_the_signed_in_detail_agree_about_an_erased_ad()
    {
        var erased = await ImportAsync(2, 30, (ad, clock) => ad.Erase(clock));
        using var signedIn = factory.CreateClient();
        signedIn.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await AuthTestHelpers.RegisterAndGetSessionIdAsync(factory, ct: Ct));

        var publicAnswer = await ProblemOf(await GetAsync(erased));
        var signedInAnswer = await ProblemOf(await signedIn.GetAsync($"/api/v1/job-ads/{erased}", Ct));

        signedInAnswer.Status.ShouldBe(HttpStatusCode.Gone, "non-vacuity: the signed-in detail still answers 410.");
        (publicAnswer.Status, publicAnswer.Title, publicAnswer.Detail)
            .ShouldBe((signedInAnswer.Status, signedInAnswer.Title, signedInAnswer.Detail),
                "one ad, one answer on every route — a different body on one route would say what the other withholds.");
    }

    [Fact]
    public async Task An_empty_id_is_a_400_that_is_not_cached()
    {
        var response = await GetAsync(Guid.Empty);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
    }

    [Fact]
    public async Task The_signed_in_job_ads_group_still_refuses_an_anonymous_caller()
    {
        var id = await ImportAsync(publishedDaysAgo: 1, expiresInDays: 30);

        var response = await _anonymous.GetAsync($"/api/v1/job-ads/{id}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
