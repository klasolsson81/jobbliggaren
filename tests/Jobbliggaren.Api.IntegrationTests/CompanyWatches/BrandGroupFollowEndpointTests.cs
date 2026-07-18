using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.CompanyWatches;

/// <summary>
/// #311 PR-5 (ADR 0087 D4) — HTTP wiring for <c>POST /api/v1/me/company-watches/brand-group</c>: auth
/// gate, JSON binding of the command, and the status mapping (NotFound → 404, Validation → 400). Runs
/// against the REAL shipped catalogue, which is DELIBERATELY EMPTY (D5b), so every well-formed slug is
/// unknown → 404 here. The successful 201 → persist → resurrect path is covered by the handler unit
/// tests (synthetic catalogue) and the real-Postgres schema/coexistence pins in
/// <see cref="CompanyWatchBrandGroupPersistenceTests"/>.
/// </summary>
[Collection("Api")]
public class BrandGroupFollowEndpointTests(ApiFactory factory)
{
    private const string Endpoint = "/api/v1/me/company-watches/brand-group";
    private readonly HttpClient _client = factory.CreateClient();

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, ct: ct);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
    }

    [Fact]
    public async Task POST_brand_group_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.PostAsJsonAsync(Endpoint, new { brandGroupId = "volvo-koncernen" }, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_brand_group_with_wellformed_but_uncurated_slug_returns_404()
    {
        // The empty shipped catalogue makes every well-formed slug unknown — proves route + command
        // binding + NotFound → 404 mapping (DomainError.ToProblemResult).
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.PostAsJsonAsync(Endpoint, new { brandGroupId = "volvo-koncernen" }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task POST_brand_group_with_malformed_slug_returns_400()
    {
        // ValidationBehavior rejects a malformed slug BEFORE the handler — proves Validation → 400.
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.PostAsJsonAsync(Endpoint, new { brandGroupId = "Volvo Koncern" }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
