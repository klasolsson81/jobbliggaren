using System.Net;
using System.Net.Http.Headers;
using JobbPilot.Api.IntegrationTests.Helpers;
using Shouldly;

namespace JobbPilot.Api.IntegrationTests.RateLimiting;

/// <summary>
/// Verifierar att list-read-policyn faktiskt returnerar 429 när PermitLimit
/// överskrids. Per CTO-rond 2026-05-13 F2-P9 + security-auditor Major-fynd
/// (OWASP API4:2023 — multi-query-DoS från komprometterat konto via
/// wildcard-LIKE-pattern). Partition på UserId (claim "sub"). StrictRateLimit-
/// factoryn sätter aggressiv test-limit (3/60s) för test-snabbhet.
/// </summary>
[Collection("ListReadRateLimit")]
public class ListReadRateLimitTests(ListReadRateLimitApiFactory factory)
{
    [Fact]
    public async Task GET_job_ads_with_auth_repeated_requests_returns_429_with_RetryAfter()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClient();

        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(client, ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

        var statusCodes = new List<HttpStatusCode>();
        HttpResponseMessage? rejected = null;

        // StrictRateLimitApiFactory sätter ListRead PermitLimit=3 → 4:e anropet
        // ska vara 429. Loopa till säkerhetstak 10 för defense-in-depth-marginal.
        for (var i = 0; i < 10; i++)
        {
            var response = await client.GetAsync("/api/v1/job-ads", ct);
            statusCodes.Add(response.StatusCode);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rejected = response;
                break;
            }
        }

        statusCodes[0].ShouldBe(HttpStatusCode.OK,
            "första anropet ska vara 200 (auth:ad + inom PermitLimit)");
        statusCodes.ShouldContain(HttpStatusCode.TooManyRequests,
            "list-read-policy ska blockera multi-query-DoS efter PermitLimit");
        rejected.ShouldNotBeNull("429 ska triggas inom 10 anrop vid PermitLimit=3");
        rejected.Headers.RetryAfter.ShouldNotBeNull(
            "RFC 6585: 429-respons ska inkludera Retry-After-header");
    }
}
