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

// C2 (ADR 0067, CTO-dom (e)) — Ssyk-paramen är BORTTAGEN ur endpointen
// (`string[]? ssyk` binder inte längre). Minimal-API-binding ignorerar
// query-parametrar som inte binds → `?ssyk=X` ger 200 OK med SAMMA resultat
// som utan param. Funktionellt identiskt med C1:s no-op (Klas-GO:at fönster
// 2026-06-09) — skillnaden är att kontraktet slutar låtsas att fältet finns.
// FE skickar `?ssyk=` tills Fas E; detta test är CTO (e):s in-block-
// verifieringskrav.
//
// SsykConceptId-KOLUMNEN + q-vägens synonym-expansion rörs INTE (testas i
// ListJobAdsFtsTests).
//
// RÖD tills JobAdsEndpoints droppat ssyk-paramen (idag binder + validerar den).
[Collection("Api")]
public class ListJobAdsSsykNoOpTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, ct: ct);
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", sessionId);
    }

    private async Task SeedImportedJobAdAsync(
        string title, string? ssykConceptId, string externalId, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var occupationJson = ssykConceptId is null
            ? "null"
            : $"{{\"concept_id\":\"{ssykConceptId}\"}}";
        var rawPayload = $"{{\"id\":\"{externalId}\",\"occupation\":{occupationJson}}}";

        var jobAd = JobAd.Import(
            title: title,
            company: Company.Create("Test Company AB").Value,
            description: "desc",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            facets: TestFacets.FromPayload(rawPayload),
            publishedAt: clock.UtcNow.AddDays(-1),
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock, declaredContacts: [], extractTerms: TestKeywordExtraction.None).Value;

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
    }

    [Fact]
    public async Task GET_job_ads_with_ssyk_param_returns_200_and_does_not_filter()
    {
        var ct = TestContext.Current.CancellationToken;
        var ssyk = $"ssyk{Guid.NewGuid():N}"[..16];
        var other = $"ssyk{Guid.NewGuid():N}"[..16];

        await SeedImportedJobAdAsync("HarSsyk", ssyk, $"ext-{Guid.NewGuid():N}", ct);
        await SeedImportedJobAdAsync("AnnanSsyk", other, $"ext-{Guid.NewGuid():N}", ct);

        await AuthenticateAsync(ct);
        // HTTP-vägen: ?ssyk=X binder INTE längre (obunden param ignoreras).
        var response = await _client.GetAsync(
            $"/api/v1/job-ads?ssyk={ssyk}&pageSize=100", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var titles = json.GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("title").GetString())
            .ToList();

        // Båda annonserna syns trots ?ssyk= (paramen är borta → ingen filtrering).
        titles.ShouldContain("HarSsyk");
        titles.ShouldContain("AnnanSsyk");
    }

    [Fact]
    public async Task GET_job_ads_with_ssyk_param_returns_same_result_as_without()
    {
        var ct = TestContext.Current.CancellationToken;
        var ssyk = $"ssyk{Guid.NewGuid():N}"[..16];
        await SeedImportedJobAdAsync("Seed", ssyk, $"ext-{Guid.NewGuid():N}", ct);

        await AuthenticateAsync(ct);
        var withSsyk = await _client.GetAsync(
            $"/api/v1/job-ads?ssyk={ssyk}&pageSize=100", ct);
        var withoutParam = await _client.GetAsync(
            "/api/v1/job-ads?pageSize=100", ct);

        withSsyk.StatusCode.ShouldBe(HttpStatusCode.OK);
        withoutParam.StatusCode.ShouldBe(HttpStatusCode.OK);

        var jsonWith = await withSsyk.Content.ReadFromJsonAsync<JsonElement>(ct);
        var jsonWithout = await withoutParam.Content.ReadFromJsonAsync<JsonElement>(ct);

        // Obunden param ändrar inte den filtrerade mängden.
        jsonWith.GetProperty("totalCount").GetInt32()
            .ShouldBe(jsonWithout.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task GET_job_ads_with_previously_invalid_ssyk_format_returns_200()
    {
        // FÖRE C2 gav `?ssyk=has space` 400 (validator-regex). EFTER C2 binder
        // paramen inte alls → ingen validering → 200. Detta är medvetet:
        // en obunden query-param är inte en del av kontraktet.
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.GetAsync("/api/v1/job-ads?ssyk=has%20space", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
