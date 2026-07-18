using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Companies;

/// <summary>
/// #560 company-search wave — POST /api/v1/companies/search end-to-end over the real
/// <c>ICompanyRegisterSearchQuery</c> (raw SQL against the Testcontainers <c>company_register</c>).
/// Proves the wire contract: auth-gate, the composed <c>companies</c>+<c>magnitude</c> envelope,
/// the <c>ValidationBehavior</c> 400 for out-of-bounds paging and a personnummer-shaped org.nr
/// (without echoing the value), and the private/no-store cache posture.
///
/// <para>
/// The shared <c>[Collection("Api")]</c> DB may already hold companies seeded by other tests, so
/// every assertion here is on SHAPE and status code — never an exact row count (contamination).
/// </para>
/// </summary>
[Collection("Api")]
public class CompanySearchEndpointTests(ApiFactory factory)
{
    private const string Endpoint = "/api/v1/companies/search";
    private const string PnrShapedOrgNr = "5501012345"; // third digit 0 → personnummer-shaped

    private readonly HttpClient _client = factory.CreateClient();

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, ct: ct);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
    }

    [Fact]
    public async Task POST_search_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.PostAsJsonAsync(Endpoint, new { }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_search_with_defaults_returns_200_with_the_companies_and_magnitude_envelope()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.PostAsJsonAsync(Endpoint, new { }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        // The (capped) page beside the honest magnitude — camelCase, so the FE can never mistake
        // the pagination count for the magnitude.
        var companies = json.GetProperty("companies");
        companies.GetProperty("items").ValueKind.ShouldBe(JsonValueKind.Array);
        companies.GetProperty("totalCount").ValueKind.ShouldBe(JsonValueKind.Number);

        var magnitude = json.GetProperty("magnitude");
        magnitude.GetProperty("magnitude").ValueKind.ShouldBe(JsonValueKind.Number);
        magnitude.GetProperty("saturated").ValueKind
            .ShouldBeOneOf(JsonValueKind.True, JsonValueKind.False);
    }

    [Fact]
    public async Task POST_search_with_page_zero_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.PostAsJsonAsync(Endpoint, new { page = 0 }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task POST_search_with_personnummer_shaped_org_number_returns_400_without_echoing_it()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.PostAsJsonAsync(
            Endpoint, new { organizationNumber = PnrShapedOrgNr }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        // Defense-in-depth (ADR 0087 D8(c)): the refusal must not reflect the typed value back.
        (await response.Content.ReadAsStringAsync(ct)).ShouldNotContain(PnrShapedOrgNr);
    }

    [Fact]
    public async Task POST_search_sets_private_no_store_cache_control()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.PostAsJsonAsync(Endpoint, new { }, ct);

        // The response varies per user and must never land in a shared proxy cache.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var cacheControl = response.Headers.CacheControl;
        cacheControl.ShouldNotBeNull();
        cacheControl.Private.ShouldBeTrue();
        cacheControl.NoStore.ShouldBeTrue();
    }
}
