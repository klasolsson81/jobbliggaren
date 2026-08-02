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

    /// <summary>
    /// The one axis that makes the request below a FILTERED search rather than a browse-all.
    /// Hoisted because CA1861 fires on a constant array written inline at an argument position —
    /// the analyzer does not ask how often the enclosing method runs, and this one runs once.
    /// </summary>
    private static readonly string[] FilteredSniCodes = ["62010"];

    [Fact]
    public async Task POST_search_UNFILTERED_returns_the_page_with_a_NULL_magnitude()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.PostAsJsonAsync(Endpoint, new { }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        // The (capped) page — camelCase, so the FE can never mistake the pagination count for
        // the magnitude.
        var companies = json.GetProperty("companies");
        companies.GetProperty("items").ValueKind.ShouldBe(JsonValueKind.Array);
        companies.GetProperty("totalCount").ValueKind.ShouldBe(JsonValueKind.Number);

        // NULL by contract, not by degradation — the ruling and the measurement behind it live in
        // GetCompanySearchMagnitudeQueryHandler, which is where the browse-all policy is decided.
        //
        // Asserting the PROPERTY EXISTS and is null, rather than that it is absent: the wire
        // shape stays stable for the FE's schema, which declares it nullable rather than optional.
        json.TryGetProperty("magnitude", out var magnitude).ShouldBeTrue();
        magnitude.ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task POST_search_FILTERED_returns_the_honest_magnitude()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        // The other half of the contract, and the reason the null above is a decision rather
        // than a regression: a filtered search still carries its number. Without this the
        // unfiltered assertion would be satisfied by an endpoint that had simply stopped
        // computing magnitudes at all.
        var response = await _client.PostAsJsonAsync(
            Endpoint, new { sniCodes = FilteredSniCodes }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        var magnitude = json.GetProperty("magnitude");
        magnitude.ValueKind.ShouldBe(JsonValueKind.Object);
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
