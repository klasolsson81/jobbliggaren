using System.Net;
using Jobbliggaren.Api.RateLimiting;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.RateLimiting;

/// <summary>
/// ADR 0182 — the related ads' bucket on a host with the production numbers: an anonymous burst past the budget ends in
/// a 429 with <c>Retry-After</c>, as uncacheable as every other answer under <c>/api/v1/public</c>. The answers before
/// the refusal are the unknown anchor's 404; the limiter is in front of the handler.
/// </summary>
[Collection("StrictRateLimit")]
public sealed class PublicJobAdRelatedRateLimitTests(StrictRateLimitApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_anonymous_burst_ends_in_an_uncacheable_429_with_Retry_After()
    {
        using var anonymous = factory.CreateClient();
        var burst = new RateLimitingOptions().PublicJobAdRelated.PermitLimit;
        var anchor = Guid.NewGuid();

        var answered = 0;
        HttpResponseMessage? rejected = null;
        for (var attempt = 0; attempt < burst * 2 && rejected is null; attempt++)
        {
            var response = await anonymous.GetAsync($"/api/v1/public/job-ads/{anchor}/related", Ct);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                rejected = response;
            else
            {
                response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
                answered++;
            }
        }

        answered.ShouldBeGreaterThanOrEqualTo(burst);
        rejected.ShouldNotBeNull();
        rejected.Headers.RetryAfter.ShouldNotBeNull();
        rejected.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
    }
}
