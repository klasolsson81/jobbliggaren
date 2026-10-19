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
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// #2191 — the employer's application address on the wire, end to end over HTTP through the real Api
/// host on real Postgres. The detail response carries it as camelCase <c>applicationUrl</c> (the
/// modal's "Ansök" link reads it); the list response never carries it. Ads are built through
/// <see cref="JobAd.Import"/>, the production funnel, never a hand-seeded column.
/// </summary>
[Collection("Api")]
public sealed class JobAdApplicationUrlReadSurfaceTests(ApiFactory factory)
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);
    private const string EmployerPage = "https://career.example.se/jobs/4567890-integration-developer";

    private readonly HttpClient _client = factory.CreateClient();

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(factory, ct: ct);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
    }

    // A unique AB-shaped org.nr, so the employer-filtered list page is exactly this ad even though the
    // [Collection("Api")] database is shared with other tests.
    private static string NewOrgNr() => $"55{Random.Shared.NextInt64(10_000_000, 99_999_999)}";

    private async Task<Guid> SeedAdAsync(string orgNr, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(T0);

        var externalId = $"applyurl-{Guid.NewGuid():N}";
        var payload = $"{{\"id\":\"{externalId}\"}}";
        var jobAd = JobAd.Import(
            title: "Integration developer",
            company: Company.Create("Exempel AB").Value,
            description: "Vi söker en integrationsutvecklare.",
            url: $"https://arbetsformedlingen.se/platsbanken/annonser/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: payload,
            facets: TestFacets.From(organizationNumber: orgNr),
            declaredContacts: [],
            publishedAt: T0,
            expiresAt: T0.AddDays(30),
            clock: clock,
            extractTerms: TestKeywordExtraction.None,
            applicationUrl: ApplicationUrl.FromSource(EmployerPage)).Value;

        jobAd.ApplicationUrl.ShouldNotBeNull("counterfactual: the ad holds an application address in the DB.");

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd.Id.Value;
    }

    [Fact]
    public async Task Get_ad_detail_serialises_the_application_url_in_camelCase()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var adId = await SeedAdAsync(NewOrgNr(), ct);

        var response = await _client.GetAsync($"/api/v1/job-ads/{adId}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("applicationUrl").GetString().ShouldBe(EmployerPage);
        json.GetProperty("url").GetString()!.ShouldStartWith("https://arbetsformedlingen.se/",
            Case.Sensitive, "url stays the ad's own page; the two are different links.");
    }

    [Fact]
    public async Task Get_job_ads_list_never_serialises_the_application_url()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var orgNr = NewOrgNr();
        var adId = await SeedAdAsync(orgNr, ct);

        var response = await _client.GetAsync($"/api/v1/job-ads?employer={orgNr}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var raw = await response.Content.ReadAsStringAsync(ct);
        raw.ShouldContain(adId.ToString(),
            customMessage: "the seeded ad must be on the page, or the absence below proves nothing.");
        raw.ShouldNotContain("applicationUrl", Case.Insensitive);
        raw.ShouldNotContain(EmployerPage);
    }
}
