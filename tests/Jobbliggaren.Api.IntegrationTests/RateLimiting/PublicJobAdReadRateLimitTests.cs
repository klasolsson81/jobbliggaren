using System.Net;
using Jobbliggaren.Api.RateLimiting;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.RateLimiting;

/// <summary>
/// ADR 0160 — the anonymous job-ad detail's bucket on a host with the production numbers: an
/// anonymous burst past the budget ends in a 429 with <c>Retry-After</c>, and the refusal is as
/// uncacheable as every other answer under <c>/api/v1/public</c>.
/// </summary>
[Collection("StrictRateLimit")]
public sealed class PublicJobAdReadRateLimitTests(StrictRateLimitApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_anonymous_burst_ends_in_an_uncacheable_429_with_Retry_After()
    {
        using var anonymous = factory.CreateClient();
        var burst = new RateLimitingOptions().PublicJobAdRead.PermitLimit;

        var answered = 0;
        HttpResponseMessage? rejected = null;
        for (var attempt = 0; attempt < burst * 2 && rejected is null; attempt++)
        {
            // An unknown id costs a token like any other read.
            var response = await anonymous.GetAsync($"/api/v1/public/job-ads/{Guid.NewGuid()}", Ct);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                rejected = response;
            else
            {
                response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
                answered++;
            }
        }

        answered.ShouldBe(burst);
        rejected.ShouldNotBeNull();
        rejected.Headers.RetryAfter.ShouldNotBeNull();
        rejected.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
    }
}
