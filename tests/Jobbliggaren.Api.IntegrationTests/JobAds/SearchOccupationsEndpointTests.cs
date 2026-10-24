using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries.GetTaxonomyTree;
using Jobbliggaren.Application.JobAds.Queries.SearchOccupations;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// #2198 (ADR 0180) — <c>GET /api/v1/job-ads/taxonomy/occupations/search?q=</c> on the real host:
/// the auth gate (the handler never runs for an anonymous caller), <c>private, no-store</c>, the
/// wire shape of both row kinds, the validator's bounds, the route's own rate-limit bucket read from
/// the built endpoint graph, and a canary term that reaches no log record on the ok, 400 and 500
/// paths (security-auditor's plan read, Major 1). The 429 path is in
/// <c>OccupationSearchRateLimitTests</c>, which needs the production bucket numbers.
/// </summary>
[Collection("Api")]
public class SearchOccupationsEndpointTests(ApiFactory factory)
{
    private const string Endpoint = "/api/v1/job-ads/taxonomy/occupations/search";

    /// <summary>A term no label holds and no fixed message can contain.</summary>
    private const string Canary = "kanariefagel2198";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpClient> AuthenticatedAsync(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(factory, ct: Ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return client;
    }

    [Fact]
    public async Task GET_without_auth_returns_401_and_the_searcher_never_runs()
    {
        var spy = new SpySearcher();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IOccupationSearcher>();
            services.AddSingleton<IOccupationSearcher>(spy);
        }));
        using var client = host.CreateClient();

        var response = await client.GetAsync($"{Endpoint}?q=projektledare", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        spy.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task GET_projektledare_returns_one_Word_row_in_the_wire_shape_and_is_private_no_store()
    {
        using var client = await AuthenticatedAsync(factory);

        var response = await client.GetAsync($"{Endpoint}?q=projektledare", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl.ShouldNotBeNull();
        response.Headers.CacheControl.Private.ShouldBeTrue();
        response.Headers.CacheControl.NoStore.ShouldBeTrue();
        response.Headers.CacheControl.Public.ShouldBeFalse();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("totalRows").GetInt32().ShouldBe(1);
        var row = body.GetProperty("rows").EnumerateArray().ShouldHaveSingleItem();
        row.GetProperty("kind").GetString().ShouldBe("Word");
        row.GetProperty("headWord").GetString().ShouldBe("Projektledare");
        // The other kind's keys are on the wire, null or empty: the shape is one for both kinds.
        row.GetProperty("groupConceptId").ValueKind.ShouldBe(JsonValueKind.Null);
        row.GetProperty("groupLabel").ValueKind.ShouldBe(JsonValueKind.Null);
        row.GetProperty("matchedNames").GetArrayLength().ShouldBe(0);
        row.GetProperty("labelHit").GetBoolean().ShouldBeFalse();
        row.GetProperty("exactMatch").GetBoolean().ShouldBeFalse();
        var variant = row.GetProperty("variants").EnumerateArray().First();
        variant.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)
            .ShouldBe(["groupConceptId", "groupLabel", "nameLabel", "occupationNameConceptId"]);
        // #2199 — Projektledare, IT's own occupation-name id, the id /jobb filters on.
        row.GetProperty("variants").EnumerateArray()
            .Single(v => v.GetProperty("nameLabel").GetString() == "Projektledare, IT")
            .GetProperty("occupationNameConceptId").GetString().ShouldBe("z5AM_ayf_WcL");
    }

    [Fact]
    public async Task GET_backend_returns_one_Group_row_in_the_wire_shape()
    {
        using var client = await AuthenticatedAsync(factory);

        var body = await client.GetFromJsonAsync<JsonElement>($"{Endpoint}?q=backend", Ct);

        var row = body.GetProperty("rows").EnumerateArray()
            .Single(r => r.GetProperty("groupConceptId").GetString() == "DJh5_yyF_hEM");
        row.GetProperty("kind").GetString().ShouldBe("Group");
        row.GetProperty("groupLabel").GetString().ShouldBe("Mjukvaru- och systemutvecklare m.fl.");
        row.GetProperty("matchedNames").EnumerateArray().Select(n => n.GetString()).ShouldContain("Backend-utvecklare");
        row.GetProperty("headWord").ValueKind.ShouldBe(JsonValueKind.Null);
        row.GetProperty("variants").GetArrayLength().ShouldBe(0);
        row.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ShouldBe(
            ["exactMatch", "groupConceptId", "groupLabel", "headWord", "kind", "labelHit", "matchedNames", "variants"]);
    }

    [Fact]
    public async Task GET_with_a_query_outside_2_to_100_characters_returns_400()
    {
        using var client = await AuthenticatedAsync(factory);

        (await client.GetAsync($"{Endpoint}?q=a", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.GetAsync(Endpoint, Ct)).StatusCode
            .ShouldBe(HttpStatusCode.BadRequest, "a missing q binds to the empty query, which the validator refuses");
        var tooLong = new string('x', SearchOccupationsQuery.MaxQueryLength + 1);
        (await client.GetAsync($"{Endpoint}?q={tooLong}", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_term_reaches_no_log_record_on_the_ok_and_400_paths()
    {
        var logs = new CapturingLoggerProvider();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<ILoggerProvider>(logs)));
        using var client = await AuthenticatedAsync(host);
        var tooLong = Canary + new string('z', SearchOccupationsQuery.MaxQueryLength);

        var ok = await client.GetAsync($"{Endpoint}?q={Canary}", Ct);
        var refused = await client.GetAsync($"{Endpoint}?q={tooLong}", Ct);

        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync(Ct)).ShouldNotContain(Canary, Case.Insensitive);
        ShouldHaveLoggedTheRequestsButNeverTheTerm(logs);
    }

    [Fact]
    public async Task The_term_reaches_no_log_record_when_the_taxonomy_store_fails()
    {
        // A database that is unavailable at cold start is a state production can reach: the index's
        // first read of the tree throws, and the request ends in a 500 whose records are captured.
        var logs = new CapturingLoggerProvider();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ILoggerProvider>(logs);
            services.RemoveAll<ITaxonomyReadModel>();
            services.AddSingleton<ITaxonomyReadModel>(new FaultingTaxonomy());
        }));
        using var client = await AuthenticatedAsync(host);

        var response = await client.GetAsync($"{Endpoint}?q={Canary}", Ct);

        ((int)response.StatusCode).ShouldBe(500);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldNotContain(Canary, Case.Insensitive);
        logs.Logs.ShouldContain(log => log.Exception != null, "the 500 path must have written its exception record");
        ShouldHaveLoggedTheRequestsButNeverTheTerm(logs);
    }

    [Fact]
    public void The_route_carries_its_own_occupation_search_policy()
    {
        _ = factory.CreateClient();

        var endpoint = factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => (e.RoutePattern.RawText ?? string.Empty)
                .EndsWith("taxonomy/occupations/search", StringComparison.Ordinal))
            .ToList()
            .ShouldHaveSingleItem();

        var policy = endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>().ShouldNotBeNull().PolicyName;
        policy.ShouldBe(RateLimitingExtensions.OccupationSearchPolicy);
        policy.ShouldNotBe(RateLimitingExtensions.TaxonomyReadPolicy);
        policy.ShouldNotBe(RateLimitingExtensions.OccupationDivisionsPolicy);
    }

    private static void ShouldHaveLoggedTheRequestsButNeverTheTerm(CapturingLoggerProvider logs)
    {
        var records = logs.Logs.ToList();
        records.ShouldContain(
            log => log.AllText.Contains(nameof(SearchOccupationsQuery), StringComparison.Ordinal),
            "the pipeline must have logged the query type, or the absence below proves nothing");
        records.Where(log => log.AllText.Contains(Canary, StringComparison.OrdinalIgnoreCase))
            .Select(log => $"{log.Category}: {log.Message}")
            .ShouldBeEmpty();
    }

    private sealed class SpySearcher : IOccupationSearcher
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public ValueTask<OccupationSearchResult> SearchAsync(string query, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return new ValueTask<OccupationSearchResult>(OccupationSearchResult.Empty);
        }
    }

    // Hand-rolled: every member faults, like a read model whose database is unreachable.
    private sealed class FaultingTaxonomy : ITaxonomyReadModel
    {
        private static InvalidOperationException Unavailable() => new("The taxonomy store is unavailable.");

        public ValueTask<TaxonomyTreeDto> GetTreeAsync(CancellationToken cancellationToken) =>
            ValueTask.FromException<TaxonomyTreeDto>(Unavailable());

        public ValueTask<IReadOnlyList<TaxonomyLabelDto>> ResolveLabelsAsync(
            IReadOnlyList<string> conceptIds, CancellationToken cancellationToken) =>
            ValueTask.FromException<IReadOnlyList<TaxonomyLabelDto>>(Unavailable());

        public ValueTask<IReadOnlyList<TaxonomySuggestionDto>> SuggestByPrefixAsync(
            string prefix, int limit, CancellationToken cancellationToken) =>
            ValueTask.FromException<IReadOnlyList<TaxonomySuggestionDto>>(Unavailable());

        public ValueTask<IReadOnlyList<string>> GetRelatedOccupationGroupsAsync(
            IReadOnlyList<string> ssyk4ConceptIds, CancellationToken cancellationToken) =>
            ValueTask.FromException<IReadOnlyList<string>>(Unavailable());

        public ValueTask<IReadOnlyList<string>> GetContainingRegionsAsync(
            IReadOnlyList<string> municipalityConceptIds, CancellationToken cancellationToken) =>
            ValueTask.FromException<IReadOnlyList<string>>(Unavailable());

        public ValueTask<IReadOnlyList<string>> GetContainingOccupationFieldsAsync(
            IReadOnlyList<string> occupationGroupConceptIds, CancellationToken cancellationToken) =>
            ValueTask.FromException<IReadOnlyList<string>>(Unavailable());
    }
}
