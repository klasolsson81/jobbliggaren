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
/// #2214 PR 3 (ADR 0183 D6) — listedness on the wire, end to end over HTTP through the real Api host
/// on real Postgres. The detail response carries camelCase <c>isListed</c>; the signed-in "Dela
/// annonsen" row renders only when it is true. This exercises the Npgsql translation of the handler's
/// <c>External?.Source</c> projection — a conditional read of an owned navigation that EF InMemory
/// runs as LINQ-to-objects and so cannot prove. Two ads, two production paths: an imported Platsbanken
/// ad (<see cref="JobAd.Import"/>, External set → listed) and a user-created ad with
/// <c>Source="Platsbanken"</c> but NO External (<see cref="JobAd.Create"/>, the <c>POST /api/v1/job-ads</c>
/// path → not listed). Both through production funnels, never a hand-seeded column.
/// </summary>
[Collection("Api")]
public sealed class JobAdListedReadSurfaceTests(ApiFactory factory)
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);

    private readonly HttpClient _client = factory.CreateClient();

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(factory, ct: ct);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
    }

    // A unique AB-shaped org.nr, so the seeded row is distinct in the shared [Collection("Api")] database.
    private static string NewOrgNr() => $"55{Random.Shared.NextInt64(10_000_000, 99_999_999)}";

    private async Task<Guid> SeedImportedPlatsbankenAdAsync(CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(T0);

        var externalId = $"listed-{Guid.NewGuid():N}";
        var payload = $"{{\"id\":\"{externalId}\"}}";
        var jobAd = JobAd.Import(
            title: "Integration developer",
            company: Company.Create("Exempel AB").Value,
            description: "Vi söker en integrationsutvecklare.",
            url: $"https://arbetsformedlingen.se/platsbanken/annonser/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: payload,
            facets: TestFacets.From(organizationNumber: NewOrgNr()),
            declaredContacts: [],
            publishedAt: T0,
            expiresAt: T0.AddDays(30),
            clock: clock,
            extractTerms: TestKeywordExtraction.None,
            applicationUrl: null).Value;

        jobAd.External.ShouldNotBeNull("counterfactual: the imported ad holds its import provenance in the DB.");

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd.Id.Value;
    }

    private async Task<Guid> SeedUserCreatedPlatsbankenAdAsync(CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(T0);

        // POST /api/v1/job-ads accepts Source="Platsbanken" and stores an ownerless row with NO
        // External. JobAd.Create is that path: it sets Source, never External.
        var jobAd = JobAd.Create(
            "User created Platsbanken ad",
            Company.Create("Exempel AB").Value,
            "Vi söker en utvecklare.",
            "https://example.com/jobb/1",
            JobSource.Platsbanken,
            T0,
            null,
            clock).Value;

        jobAd.External.ShouldBeNull("counterfactual: the user-created ad holds no import provenance.");

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd.Id.Value;
    }

    [Fact]
    public async Task Get_ad_detail_marks_an_imported_platsbanken_ad_as_listed()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var adId = await SeedImportedPlatsbankenAdAsync(ct);

        var response = await _client.GetAsync($"/api/v1/job-ads/{adId}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("isListed").GetBoolean().ShouldBeTrue(
            "an imported Platsbanken ad, Active and not past its deadline, is publicly listed.");
    }

    [Fact]
    public async Task Get_ad_detail_marks_a_user_created_platsbanken_ad_as_not_listed()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var adId = await SeedUserCreatedPlatsbankenAdAsync(ct);

        var response = await _client.GetAsync($"/api/v1/job-ads/{adId}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("isListed").GetBoolean().ShouldBeFalse(
            "Source is user-settable; without an import (External) the ad was never public, so the real "
            + "Npgsql projection of External?.Source distinguishes it from the imported ad above (SA-2 M-2).");
    }
}
