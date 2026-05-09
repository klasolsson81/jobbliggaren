using System.Net;
using System.Net.Http.Json;
using Shouldly;

namespace JobbPilot.Api.IntegrationTests.RateLimiting;

/// <summary>
/// Verifierar att auth-write-policyn (login + register) faktiskt returnerar
/// 429 när PermitLimit överskrids (TD-21 Sec-Major-2). Använder
/// <see cref="StrictRateLimitApiFactory"/> som inte höjer rate-limits via
/// env-overlay — defaults gäller (20/min per IP).
///
/// Test isoleras i egen <c>[Collection]</c> så env-var-flippen inte krockar
/// med <c>ApiFactory</c>-baserade tester som körs parallellt.
/// </summary>
[Collection("StrictRateLimit")]
public class AuthWriteRateLimitTests(StrictRateLimitApiFactory factory)
{
    [Fact]
    public async Task POST_login_with_repeated_failed_attempts_eventually_returns_429()
    {
        // Default AuthWrite-limit är 20/min per IP. localhost-partition delas mellan
        // alla anrop i denna test — efter 20 attempts ska 21:a vara 429, inte 401.
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClient();
        var email = $"rl-{Guid.NewGuid()}@example.com";

        var statusCodes = new List<HttpStatusCode>();
        for (var i = 0; i < 25; i++)
        {
            var response = await client.PostAsJsonAsync(
                "/api/v1/auth/login",
                new { email, password = "wrong-password" },
                ct);
            statusCodes.Add(response.StatusCode);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                break;
        }

        statusCodes.ShouldContain(HttpStatusCode.TooManyRequests,
            "auth-write-policy ska blockera credential-stuffing efter PermitLimit");
        // Före 429: 401 (Unauthorized — fel password), inte 400/500
        statusCodes[0].ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Response_429_includes_RetryAfter_header()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClient();
        var email = $"rl-retry-{Guid.NewGuid()}@example.com";

        HttpResponseMessage? rejected = null;
        for (var i = 0; i < 30; i++)
        {
            var response = await client.PostAsJsonAsync(
                "/api/v1/auth/login",
                new { email, password = "wrong-password" },
                ct);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rejected = response;
                break;
            }
        }

        rejected.ShouldNotBeNull("429 ska triggas inom 30 anrop");
        rejected.Headers.RetryAfter.ShouldNotBeNull(
            "RFC 6585: 429-respons ska inkludera Retry-After-header");
    }
}
