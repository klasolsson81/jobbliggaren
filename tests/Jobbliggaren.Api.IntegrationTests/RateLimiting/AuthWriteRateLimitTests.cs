using System.Net;
using System.Net.Http.Json;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.RateLimiting;

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
    // Default AuthWrite-limit är 20/min per IP. Tidigare separat två tester (en
    // för 401→429-sekvens, en för Retry-After-header) som delade rate-limit-budget
    // via samma StrictRateLimitApiFactory-instans → andra testet fick 429 direkt
    // på första anropet eftersom första testet förbrukat budgeten (1-minuts
    // window återställs inte mellan tester). Hopslagna här eftersom båda asserts
    // gäller samma 429-trigger-pipeline.
    [Fact]
    public async Task POST_login_with_repeated_failed_attempts_returns_429_with_RetryAfter()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClient();
        var email = $"rl-{Guid.NewGuid()}@example.com";

        var statusCodes = new List<HttpStatusCode>();
        HttpResponseMessage? rejected = null;

        for (var i = 0; i < 30; i++)
        {
            var response = await client.PostAsJsonAsync(
                "/api/v1/auth/login",
                new { email, password = "wrong-password" },
                ct);
            statusCodes.Add(response.StatusCode);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rejected = response;
                break;
            }
        }

        statusCodes[0].ShouldBe(HttpStatusCode.Unauthorized,
            "första anropet ska vara 401 (fel password) innan rate-limit kickar in");
        statusCodes.ShouldContain(HttpStatusCode.TooManyRequests,
            "auth-write-policy ska blockera credential-stuffing efter PermitLimit");
        rejected.ShouldNotBeNull("429 ska triggas inom 30 anrop");
        rejected.Headers.RetryAfter.ShouldNotBeNull(
            "RFC 6585: 429-respons ska inkludera Retry-After-header");
    }
}
