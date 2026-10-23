using System.Net;
using System.Net.Http.Headers;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.RateLimiting;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.RateLimiting;

/// <summary>
/// #2198 (ADR 0180) — the occupation search's own bucket on a host with the production numbers: one
/// user's burst ends in a 429 with Retry-After and no queue, another user's bucket is untouched, and
/// the refused request's term reaches neither its answer nor a log record.
/// </summary>
[Collection("StrictRateLimit")]
public sealed class OccupationSearchRateLimitTests(StrictRateLimitApiFactory factory)
{
    private const string Endpoint = "/api/v1/job-ads/taxonomy/occupations/search";

    /// <summary>A term no label holds, so no answer and no record carries it by accident.</summary>
    private const string Canary = "ratesentinel2198";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpClient> UserAsync()
    {
        var client = factory.CreateClient();
        var session = await AuthTestHelpers.RegisterAndGetSessionIdAsync(factory, ct: Ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session);
        return client;
    }

    [Fact]
    public async Task One_user_s_burst_ends_in_429_with_Retry_After_while_another_user_still_searches()
    {
        using var first = await UserAsync();
        using var second = await UserAsync();
        var burst = new RateLimitingOptions().OccupationSearch.PermitLimit;

        var answered = 0;
        HttpResponseMessage? rejected = null;
        for (var attempt = 0; attempt < burst * 2 && rejected is null; attempt++)
        {
            var response = await first.GetAsync($"{Endpoint}?q={Canary}", Ct);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                rejected = response;
            else
            {
                response.StatusCode.ShouldBe(HttpStatusCode.OK);
                answered++;
            }
        }

        answered.ShouldBeGreaterThanOrEqualTo(burst);
        rejected.ShouldNotBeNull();
        rejected.Headers.RetryAfter.ShouldNotBeNull();
        (await rejected.Content.ReadAsStringAsync(Ct)).ShouldNotContain(Canary, Case.Insensitive);
        (await second.GetAsync($"{Endpoint}?q=projektledare", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var logs = factory.Logs.Logs.ToList();
        logs.ShouldContain(log =>
            log.AllText.Contains("Rate limit exceeded", StringComparison.Ordinal)
            && log.AllText.Contains(Endpoint, StringComparison.Ordinal));
        logs.Where(log => log.AllText.Contains(Canary, StringComparison.OrdinalIgnoreCase))
            .Select(log => $"{log.Category}: {log.Message}")
            .ShouldBeEmpty();
    }
}
