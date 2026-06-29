using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

// #383 (CTO-bind cto-7f3a9c2e1b4d8a6f, Approach B) — endpoint-bindning + validering för
// status-filtret (?savedOnly=/?appliedOnly=/?hideApplied=). Verifierar att minimal-API
// binder de tre bool-flaggorna och att validator-mutex:en (appliedOnly ∧ hideApplied)
// håller hela vägen via HTTP (rent 400, ingen tyst tom sida). En FÄRSK användare har
// inga sparade/ansökta annonser → de giltiga kombinationerna returnerar 200 med en
// (sannolikt tom) sida; vi asserterar status-koderna + att body deserialiseras, ALDRIG
// specifika annonser. Den FAKTISKA EXISTS/NOT EXISTS-gallringen (set-likhet, seeker-
// isolering, count, paginering, grad-komposition) pinnas i
// ListJobAdsStatusFilterOracleTests; här är scopet bindning + 400-grinden.
[Collection("Api")]
public class ListJobAdsStatusFilterEndpointTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, ct: ct);
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", sessionId);
    }

    // En giltig status-kombination ska binda och returnera en deserialiserbar sida.
    private async Task AssertBindsAndDeserializesAsync(string queryString, CancellationToken ct)
    {
        var response = await _client.GetAsync(queryString, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK,
            $"giltig status-kombination ({queryString}) ska binda och returnera 200.");

        // Body deserialiseras som en paginerad sida (en färsk användare → sannolikt tom,
        // men kontraktet ska hålla — vi asserterar ALDRIG specifika annonser).
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.TryGetProperty("items", out var items).ShouldBeTrue(
            "svaret bär en camelCase `items`-array (PagedResult-kontraktet FE läser).");
        items.ValueKind.ShouldBe(JsonValueKind.Array);
        json.TryGetProperty("totalCount", out _).ShouldBeTrue(
            "svaret bär `totalCount` (PagedResult-kontraktet).");
    }

    [Fact]
    public async Task GET_job_ads_with_savedOnly_binds_and_returns_200()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        await AssertBindsAndDeserializesAsync("/api/v1/job-ads?savedOnly=true&pageSize=50", ct);
    }

    [Fact]
    public async Task GET_job_ads_with_appliedOnly_binds_and_returns_200()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        await AssertBindsAndDeserializesAsync("/api/v1/job-ads?appliedOnly=true&pageSize=50", ct);
    }

    [Fact]
    public async Task GET_job_ads_with_hideApplied_binds_and_returns_200()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        await AssertBindsAndDeserializesAsync("/api/v1/job-ads?hideApplied=true&pageSize=50", ct);
    }

    [Fact]
    public async Task GET_job_ads_with_savedOnly_and_hideApplied_is_valid_combo_returns_200()
    {
        // "Sparade jag inte sökt ännu" — giltig kombination (savedOnly + NOT EXISTS ansökt).
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        await AssertBindsAndDeserializesAsync(
            "/api/v1/job-ads?savedOnly=true&hideApplied=true&pageSize=50", ct);
    }

    [Fact]
    public async Task GET_job_ads_with_savedOnly_and_appliedOnly_is_valid_union_returns_200()
    {
        // Union (OR) — sparade ELLER ansökta. Giltig.
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        await AssertBindsAndDeserializesAsync(
            "/api/v1/job-ads?savedOnly=true&appliedOnly=true&pageSize=50", ct);
    }

    [Fact]
    public async Task GET_job_ads_with_appliedOnly_and_hideApplied_is_rejected_400()
    {
        // Validator-mutex:en via HTTP: "visa endast ansökta" OCH "dölj ansökta" samtidigt
        // är självmotsägande → rent 400 (i stället för en tyst tom sida).
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.GetAsync(
            "/api/v1/job-ads?appliedOnly=true&hideApplied=true&pageSize=50", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GET_job_ads_without_status_filter_returns_200()
    {
        // Inget status-filter alls (default false) → den anonyma, cachebara sök-vägen.
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        await AssertBindsAndDeserializesAsync("/api/v1/job-ads?pageSize=50", ct);
    }

    [Fact]
    public async Task GET_job_ads_with_savedOnly_anonymous_returns_401()
    {
        // Hela /job-ads-gruppen är RequireAuthorization-gated — anonym → 401 före handlern.
        var ct = TestContext.Current.CancellationToken;
        var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync("/api/v1/job-ads?savedOnly=true&pageSize=50", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
