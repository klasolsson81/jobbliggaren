using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using JobbPilot.Api.IntegrationTests.Helpers;
using JobbPilot.Api.IntegrationTests.Infrastructure;
using JobbPilot.Application.JobAds.Abstractions;
using JobbPilot.Application.JobAds.Queries.ListJobAds;
using JobbPilot.Domain.Common;
using JobbPilot.Domain.JobAds;
using JobbPilot.Infrastructure.JobAds;
using JobbPilot.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Api.IntegrationTests.JobAds;

// C1 (ADR 0067 Platsbanken sök-paritet) — Variant C nivåbyte. Den explicita
// Ssyk-equality-grenen TAS BORT ur ApplyCriteria: yrke-filtret targetar numera
// OccupationGroupConceptId (ssyk-level-4), inte SsykConceptId (occupation-name).
//
// SsykConceptId-KOLUMNEN bevaras (q-vägens synonym-expansion mot SsykConceptId
// rörs INTE — den är separat och testas i ListJobAdsFtsTests). Men en explicit
// ?ssyk=X / OccupationGroup-oberoende Ssyk-param ska INTE längre filtrera på
// egen hand. Param finns kvar (deprecerad no-op, bakåtkompat-binding) men
// ApplyCriteria ignorerar den.
//
// no-op-semantik (verifierad mot architect-design Variant C): en query med
// ENBART Ssyk angiven motsvarar en query UTAN filter → samtliga aktiva annonser
// returneras (Ssyk varken filtrerar bort eller in). q-vägen är opåverkad.
//
// RÖD tills JobAdSearchQuery.ApplyCriteria tar bort Ssyk-equality-grenen.
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
            publishedAt: clock.UtcNow.AddDays(-1),
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock).Value;

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
    }

    private static ListJobAdsQueryHandler CreateHandler(IServiceScope scope) =>
        new(new JobAdSearchQuery(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            Substitute.For<IOccupationSynonymExpander>()));

    [Fact]
    public async Task ApplyCriteria_SsykOnly_DoesNotFilter_ReturnsBothMatchingAndNonMatchingByOccupation()
    {
        var ct = TestContext.Current.CancellationToken;
        var ssyk = $"ssyk{Guid.NewGuid():N}"[..16];
        var other = $"ssyk{Guid.NewGuid():N}"[..16];

        // En annons med occupation.concept_id=ssyk + en med annan occupation.
        await SeedImportedJobAdAsync("HarSsyk", ssyk, $"ext-{Guid.NewGuid():N}", ct);
        await SeedImportedJobAdAsync("AnnanSsyk", other, $"ext-{Guid.NewGuid():N}", ct);

        using var scope = _factory.Services.CreateScope();
        var handler = CreateHandler(scope);

        var result = await handler.Handle(new ListJobAdsQuery(Ssyk: [ssyk]), ct);

        // Variant C: Ssyk-grenen borta → ingen filtrering. BÅDA annonserna
        // (och eventuella andra aktiva) återfinns; mängden krymper INTE till
        // bara den med matchande occupation.concept_id.
        var titles = result.Items.Select(i => i.Title).ToList();
        titles.ShouldContain("HarSsyk");
        titles.ShouldContain("AnnanSsyk");
    }

    [Fact]
    public async Task ApplyCriteria_SsykOnly_IsEquivalentToNoFilter()
    {
        var ct = TestContext.Current.CancellationToken;
        var ssyk = $"ssyk{Guid.NewGuid():N}"[..16];
        await SeedImportedJobAdAsync("Seed", ssyk, $"ext-{Guid.NewGuid():N}", ct);

        using var scope = _factory.Services.CreateScope();
        var handler = CreateHandler(scope);

        // En stor pageSize så hela aktiva mängden ryms i totalCount-jämförelsen.
        var withSsyk = await handler.Handle(
            new ListJobAdsQuery(PageSize: 100, Ssyk: [ssyk]), ct);
        var withoutFilter = await handler.Handle(
            new ListJobAdsQuery(PageSize: 100), ct);

        // no-op: Ssyk-param ändrar inte den filtrerade mängden.
        withSsyk.TotalCount.ShouldBe(withoutFilter.TotalCount);
    }

    [Fact]
    public async Task GET_job_ads_with_ssyk_filter_no_longer_filters_on_its_own()
    {
        var ct = TestContext.Current.CancellationToken;
        var ssyk = $"ssyk{Guid.NewGuid():N}"[..16];
        var other = $"ssyk{Guid.NewGuid():N}"[..16];

        await SeedImportedJobAdAsync("HarSsyk", ssyk, $"ext-{Guid.NewGuid():N}", ct);
        await SeedImportedJobAdAsync("AnnanSsyk", other, $"ext-{Guid.NewGuid():N}", ct);

        await AuthenticateAsync(ct);
        // HTTP-vägen: ?ssyk=X binder fortfarande (bakåtkompat) men ska EJ filtrera.
        var response = await _client.GetAsync(
            $"/api/v1/job-ads?ssyk={ssyk}&pageSize=100", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var titles = json.GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("title").GetString())
            .ToList();

        // Båda annonserna syns trots ?ssyk= (no-op).
        titles.ShouldContain("HarSsyk");
        titles.ShouldContain("AnnanSsyk");
    }
}
