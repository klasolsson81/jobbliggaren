using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.JobAds.Queries.GetTaxonomyTree;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

// ADR 0043 — picker-träd + reverse-lookup-endpoints. Verifierar mot riktig
// host (ApiFactory): auth-gate (samma group som RequireAuthorization),
// ETag + Cache-Control: private, 304 vid If-None-Match, reverse-lookup
// inkl graceful "Okänd kod"-fallback. Speglar ListJobAdsTests/SuggestJobAdTerms-
// integrationsmönstret. Taxonomi-snapshoten seedas av TaxonomySnapshotSeeder
// (IHostedService) vid host-start (Test-env grace-period).
[Collection("Api")]
public class GetTaxonomyEndpointTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, ct: ct);
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", sessionId);
    }

    [Fact]
    public async Task GET_taxonomy_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.GetAsync("/api/v1/job-ads/taxonomy", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_taxonomy_labels_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.GetAsync(
            "/api/v1/job-ads/taxonomy/labels?ids=r-1", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_taxonomy_with_auth_returns_200_with_tree_shape_etag_and_private_cache()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.GetAsync("/api/v1/job-ads/taxonomy", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        response.Headers.ETag.ShouldNotBeNull();
        response.Headers.ETag!.IsWeak.ShouldBeTrue(); // W/"..." svag ETag

        // Auth-gated → ALDRIG shared/public cache (Web Cache Deception, MAP-3).
        response.Headers.CacheControl!.Private.ShouldBeTrue();
        response.Headers.CacheControl.Public.ShouldBeFalse();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.ValueKind.ShouldBe(JsonValueKind.Object);
        json.TryGetProperty("regions", out var regions).ShouldBeTrue();
        regions.ValueKind.ShouldBe(JsonValueKind.Array);
        json.TryGetProperty("occupationFields", out var fields).ShouldBeTrue();
        fields.ValueKind.ShouldBe(JsonValueKind.Array);
    }

    [Fact]
    public async Task GET_taxonomy_exposes_municipality_and_occupationGroup_cascade()
    {
        // C1 (ADR 0067 Platsbanken sök-paritet) — additiv kaskad i JSON-svaret:
        //   regions[].municipalities[] (kommun under län)
        //   occupationFields[].occupationGroups[] (yrkesgrupp under yrkesområde)
        //   occupationFields[].occupations[] BEHÅLLS (recall-substrat).
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.GetAsync("/api/v1/job-ads/taxonomy", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        // Minst ett län bär municipalities-array med innehåll.
        var regionWithMunicipalities = json.GetProperty("regions").EnumerateArray()
            .FirstOrDefault(r =>
                r.TryGetProperty("municipalities", out var m)
                && m.ValueKind == JsonValueKind.Array
                && m.GetArrayLength() > 0);
        regionWithMunicipalities.ValueKind.ShouldNotBe(JsonValueKind.Undefined);
        var municipality = regionWithMunicipalities.GetProperty("municipalities")[0];
        municipality.GetProperty("conceptId").GetString().ShouldNotBeNullOrWhiteSpace();
        municipality.GetProperty("label").GetString().ShouldNotBeNullOrWhiteSpace();

        // Minst ett yrkesområde bär occupationGroups-array med innehåll, OCH
        // occupations-arrayen finns kvar (additiv kaskad).
        var fields = json.GetProperty("occupationFields").EnumerateArray().ToList();
        var fieldWithGroups = fields.FirstOrDefault(f =>
            f.TryGetProperty("occupationGroups", out var g)
            && g.ValueKind == JsonValueKind.Array
            && g.GetArrayLength() > 0);
        fieldWithGroups.ValueKind.ShouldNotBe(JsonValueKind.Undefined);
        var group = fieldWithGroups.GetProperty("occupationGroups")[0];
        group.GetProperty("conceptId").GetString().ShouldNotBeNullOrWhiteSpace();
        group.GetProperty("label").GetString().ShouldNotBeNullOrWhiteSpace();

        // occupations-fältet bevaras på alla yrkesområden.
        fields.ShouldAllBe(f => HasArrayProperty(f, "occupations"));
    }

    // Hjälpare: out-var får inte deklareras i ett expression tree (CS8198) —
    // ShouldAllBe tar en Expression<Func<...>>, så TryGetProperty-anropet flyttas
    // till en vanlig metod som expression-trädet kan kalla.
    private static bool HasArrayProperty(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value)
           && value.ValueKind == JsonValueKind.Array;

    [Fact]
    public async Task GET_taxonomy_with_matching_If_None_Match_returns_304()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var first = await _client.GetAsync("/api/v1/job-ads/taxonomy", ct);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        var etag = first.Headers.ETag!.ToString();

        var conditional = new HttpRequestMessage(
            HttpMethod.Get, "/api/v1/job-ads/taxonomy");
        conditional.Headers.TryAddWithoutValidation("If-None-Match", etag);
        var second = await _client.SendAsync(conditional, ct);

        second.StatusCode.ShouldBe(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task GET_taxonomy_labels_resolves_known_and_unknown_ids_gracefully()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        // Hämta ett känt concept-id ur trädet.
        var treeResp = await _client.GetAsync("/api/v1/job-ads/taxonomy", ct);
        var tree = await treeResp.Content.ReadFromJsonAsync<JsonElement>(ct);
        var knownId = tree.GetProperty("regions")[0]
            .GetProperty("conceptId").GetString()!;

        var response = await _client.GetAsync(
            $"/api/v1/job-ads/taxonomy/labels?ids={knownId}&ids=helt-okand-77", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.Private.ShouldBeTrue();

        var labels = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        labels.ValueKind.ShouldBe(JsonValueKind.Array);
        labels.GetArrayLength().ShouldBe(2);

        var byId = labels.EnumerateArray()
            .ToDictionary(
                e => e.GetProperty("conceptId").GetString()!,
                e => e.GetProperty("label").GetString()!);

        byId[knownId].ShouldNotBeNullOrWhiteSpace();
        byId[knownId].ShouldNotStartWith("Okänd kod"); // känt → riktigt namn
        byId["helt-okand-77"].ShouldBe("Okänd kod (helt-okand-77)");
    }

    [Fact]
    public async Task GET_taxonomy_labels_over_cap_returns_400()
    {
        // DoS-cap (ResolveTaxonomyLabelsQueryValidator = MaxConceptIds ×4 efter
        // C1, ADR 0067) enforce:as i Validation-pipeline → 400 innan handlern.
        // Antalet härleds från konstanten (DRY) — cap+1 → over-cap → 400.
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var overCap = ResolveTaxonomyLabelsQueryValidator.MaxConceptIdsPerCall + 1;
        var ids = string.Join("&",
            Enumerable.Range(0, overCap).Select(i => $"ids=id{i}"));

        var response = await _client.GetAsync(
            $"/api/v1/job-ads/taxonomy/labels?{ids}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GET_taxonomy_labels_with_empty_ids_returns_200_empty()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.GetAsync(
            "/api/v1/job-ads/taxonomy/labels", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var labels = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        labels.ValueKind.ShouldBe(JsonValueKind.Array);
        labels.GetArrayLength().ShouldBe(0);
    }
}
