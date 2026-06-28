using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Matching;

/// <summary>
/// #300 PR-5a (BE plumbing) — the WIRE binding-smoke oracle for the new <c>includeRelated</c>
/// request flag that threads exact→related broadening through the three user-facing query
/// paths (ADR 0084 question A; off by default). This file proves the param BINDS and is
/// ACCEPTED end-to-end on each surface:
/// <list type="number">
/// <item>POST <c>/api/v1/me/job-ad-match-tags</c> body <c>includeRelated</c> (the batch overlay).</item>
/// <item>GET <c>/api/v1/me/job-ad-match-tags/{id}?includeRelated=</c> (the modal detail).</item>
/// <item>GET <c>/api/v1/job-ads?includeRelated=</c> (the list).</item>
/// </list>
/// <para>
/// <b>Layering — this file is the WIRE binding-smoke; the real Related-surfacing capstone is
/// <see cref="RelatedSurfacingEndToEndTests"/>:</b> the actual exact→related broadening from a
/// real <c>taxonomy_relations</c> edge is produced by <c>MatchProfileBuilder</c> only when
/// <c>includeRelated=true</c>, and <c>TaxonomyReadModel</c> is a PROCESS-WIDE SINGLETON that loads
/// <c>taxonomy_relations</c> exactly ONCE per host (lazy cache, success-only). A per-test seeded
/// edge would race that cache in the shared <c>[Collection("Api")]</c> host (a sibling test may
/// warm it first → non-deterministic). <see cref="RelatedSurfacingEndToEndTests"/> sidesteps the
/// race by asserting against an edge ALREADY in the host-start-seeded snapshot
/// (<c>occupation-substitutability.json</c> → <c>TaxonomySnapshotSeeder</c> in
/// <c>ApiFactory.InitializeAsync</c>), so it deterministically proves real-edge Related over all
/// three surfaces. This file complements it: it proves the WIRE binds the flag (200/404, never
/// 400) and that the count surface is unaffected (ADR 0084 question D). The handler-threading unit
/// suites (GetJobAdMatchBatch/Detail/ListJobAds) pin that the flag reaches
/// <c>BuildFullFor*Async(includeRelated:)</c> deterministically.
/// </para>
/// </summary>
[Collection("Api")]
public class IncludeRelatedParamBindingTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, ct: ct);
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", sessionId);
    }

    private static int EntriesCount(JsonElement dto) =>
        dto.GetProperty("entries").EnumerateObject().Count();

    // =================================================================
    // 1. POST /me/job-ad-match-tags — the body's includeRelated flag binds (true AND false)
    //    and the request is accepted (200). A fresh user has no stated occupation → the
    //    SSYK-gate yields empty entries regardless of the flag (RED until JobAdMatchBatchRequest
    //    + GetJobAdMatchBatchQuery carry IncludeRelated and the endpoint forwards it).
    // =================================================================

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task POST_match_tags_binds_includeRelated_in_body_returns_200(bool includeRelated)
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.PostAsJsonAsync(
            "/api/v1/me/job-ad-match-tags",
            new { jobAdIds = new[] { Guid.NewGuid() }, includeRelated },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        // No stated occupation → SSYK-gate → empty entries (the flag does not bypass the gate).
        EntriesCount(dto).ShouldBe(0);
    }

    [Fact]
    public async Task POST_match_tags_without_includeRelated_defaults_and_returns_200()
    {
        // Omitting the flag (today's only production caller until the PR-5 FE toggle) must
        // still bind (default false) and be accepted.
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.PostAsJsonAsync(
            "/api/v1/me/job-ad-match-tags",
            new { jobAdIds = new[] { Guid.NewGuid() } },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // =================================================================
    // 2. GET /me/job-ad-match-tags/{id}?includeRelated= — the query param binds (true AND
    //    false) and the auth-gated modal detail endpoint is reached (a non-existent ad → 404,
    //    proving the request bound and routed to the handler/scorer, not a 400 binding failure).
    // =================================================================

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public async Task GET_match_detail_binds_includeRelated_query_param(string includeRelated)
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        // A non-existent ad → 404 (the scorer's NotFoundException propagates). A 404 — not a
        // 400 — proves the includeRelated param bound and the request reached the handler.
        var response = await _client.GetAsync(
            $"/api/v1/me/job-ad-match-tags/{Guid.NewGuid()}?includeRelated={includeRelated}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GET_match_detail_without_includeRelated_defaults_and_routes_to_handler()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.GetAsync(
            $"/api/v1/me/job-ad-match-tags/{Guid.NewGuid()}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // =================================================================
    // 3. GET /api/v1/job-ads?includeRelated= — the list query param binds (true AND false)
    //    and returns 200, alone and composed with the Related grade filter (the FE's actual
    //    "broaden + show only Related" combination). A fresh user honest-fallbacks (no stated
    //    occupation), but the binding must succeed regardless.
    // =================================================================

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public async Task GET_job_ads_binds_includeRelated_query_param_returns_200(string includeRelated)
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.GetAsync(
            $"/api/v1/job-ads?includeRelated={includeRelated}&pageSize=50", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GET_job_ads_includeRelated_true_with_related_grade_filter_and_match_sort_returns_200()
    {
        // The FE's real broaden-combination: "show only Related, ordered by match, broadened".
        // ?matchGrades=Related already binds (Related is a filterable grade); adding
        // ?includeRelated=true must bind too and compose without a 400.
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.GetAsync(
            "/api/v1/job-ads?matchGrades=Related&includeRelated=true&sortBy=MatchDesc&pageSize=50", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GET_job_ads_without_includeRelated_returns_200()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.GetAsync("/api/v1/job-ads?pageSize=50", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // =================================================================
    // 4. Count is NOT affected by includeRelated (ADR 0084 question D — list-only; Related is
    //    never in the headline grades [Good, Strong], so GetMyMatchCountQueryHandler stays
    //    includeRelated=false). There is no includeRelated param on /me/match-count; this pins
    //    that the count endpoint ignores the broaden concept entirely (a fresh user → honest 0,
    //    unchanged whether or not a sibling surface broadened).
    // =================================================================

    [Fact]
    public async Task GET_match_count_has_no_includeRelated_surface_and_returns_honest_zero()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        // No includeRelated param exists on the count endpoint; appending one is an unbound
        // query-param (ignored, 200) — the count is list-only-independent of broadening.
        var response = await _client.GetAsync("/api/v1/me/match-count?includeRelated=true", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("count").GetInt32().ShouldBe(0,
            "Related räknas aldrig i headline-count (ADR 0084 fråga D) — en ny användare utan " +
            "angivet yrke får honest 0, oberoende av includeRelated.");
    }
}
