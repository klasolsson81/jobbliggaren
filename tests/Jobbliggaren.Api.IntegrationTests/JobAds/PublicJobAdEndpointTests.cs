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
        "municipalityConceptId", "occupationGroupConceptId", "publishedAt", "regionConceptId",
        "title", "url", "worktimeExtentConceptId",
    ];

    private readonly HttpClient _anonymous = factory.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Guid> ImportAsync(
        int publishedDaysAgo, int? expiresInDays, Func<JobAd, IDateTimeProvider, Result>? then = null)
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
                occupationGroup: "grp_public", municipality: "mun_public", region: "reg_public",
                employmentType: "et_public", worktimeExtent: "wt_public", organizationNumber: "5560000000"),
            declaredContacts: [],
            publishedAt: clock.UtcNow.AddDays(-publishedDaysAgo),
            expiresAt: expiresInDays is { } days ? clock.UtcNow.AddDays(days) : null,
            clock: clock,
            extractTerms: TestKeywordExtraction.None).Value;

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
            await ProblemOf(await GetAsync(archived)),
            await ProblemOf(await GetAsync(pastDeadline)),
            await ProblemOf(await GetAsync(erased)),
        };

        answers[0].Status.ShouldBe(HttpStatusCode.Gone);
        answers[0].Title.ShouldBe("JobAd.Gone");
        answers[0].Detail.ShouldBe("Annonsen är inte längre tillgänglig.");
        answers[0].Cache.ShouldBe("no-store");
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

        var manual = await ProblemOf(await GetAsync(manualId));
        var unknown = await ProblemOf(await GetAsync(Guid.NewGuid()));

        unknown.Status.ShouldBe(HttpStatusCode.NotFound);
        unknown.Cache.ShouldBe("no-store");
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
