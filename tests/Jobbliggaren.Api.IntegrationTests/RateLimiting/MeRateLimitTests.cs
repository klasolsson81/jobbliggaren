using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.RateLimiting;

/// <summary>
/// Verifierar de tre nya "me"-policyerna (Pre-4 STEG 5 — TD-87 + TD-92):
/// MeListRead, JobAdStatusBatch och MeWrite. Factoryn sätter aggressiv
/// test-limit (3/60s) på alla tre via env-overlay.
///
/// Partition-design (låst av dotnet-architect + senior-cto-advisor):
/// - MeListRead + MeWrite: partition per UserId (claim "sub"), anonym → NoLimiter.
/// - JobAdStatusBatch: DUAL partition — sub närvarande → user:-bucket,
///   annars → ip:-bucket. Den anonyma ip:-fallbacken är TD-87:s kritiska
///   skyddsegenskap (endpoint är INTE auth-gated; alla UserId-policyer
///   NoLimiter:ar anonyma → utan ip:-fallback vore ytan helt oskyddad).
///
/// Bucket-ordning (jfr AuthWriteRateLimitTests-kommentaren): 1-minuters-fönstret
/// återställs INTE mellan tester. Varje UserId-partitionerad Fact får därför en
/// FÄRSK registrerad user (unik sub → unik bucket) så de inte delar budget. Det
/// anonyma JobAdStatusBatch-testet delar den process-globala 127.0.0.1-IP-bucketen
/// och måste vara den ENDA anonyma konsumenten av den policyns ip:-bucket → hålls
/// i sin egen Fact.
/// </summary>
[Collection("MeRateLimit")]
public class MeRateLimitTests(MeRateLimitApiFactory factory)
{
    [Fact]
    public async Task GET_me_profile_with_auth_repeated_requests_returns_429_with_RetryAfter()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClient();

        // Egen user → unik UserId-partition (delar inte budget med övriga Facts).
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(client, ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

        var statusCodes = new List<HttpStatusCode>();
        HttpResponseMessage? rejected = null;

        // MeListRead PermitLimit=3 → 4:e anropet ska vara 429. Loopa till tak 10
        // för defense-in-depth-marginal.
        for (var i = 0; i < 10; i++)
        {
            var response = await client.GetAsync("/api/v1/me/profile", ct);
            statusCodes.Add(response.StatusCode);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rejected = response;
                break;
            }
        }

        // Profilen kan vara tom för nyregistrerad user → 404 är giltigt. Det enda
        // som spec:ar är att FÖRSTA anropet INTE rate-limit:as (inom PermitLimit).
        statusCodes[0].ShouldNotBe(HttpStatusCode.TooManyRequests,
            "första anropet ska vara inom PermitLimit (200 eller 404), inte 429");
        statusCodes.ShouldContain(HttpStatusCode.TooManyRequests,
            "me-list-read-policy ska blockera multi-query-DoS efter PermitLimit");
        rejected.ShouldNotBeNull("429 ska triggas inom 10 anrop vid PermitLimit=3");
        rejected.Headers.RetryAfter.ShouldNotBeNull(
            "RFC 6585: 429-respons ska inkludera Retry-After-header");
    }

    [Fact]
    public async Task POST_job_ad_status_anonymous_repeated_requests_returns_429_with_RetryAfter()
    {
        // KRITISK TD-87-egenskap: POST /api/v1/me/job-ad-status är anonym-tolerant
        // (INTE .RequireAuthorization()-gated — handler returnerar tom DTO utan
        // UserId). Utan en ip:-fallback-partition i JobAdStatusBatch-policyn skulle
        // anonyma anrop NoLimiter:as som alla andra UserId-policyer → ytan vore helt
        // oskyddad mot anonym DoS. Detta test bevisar att ip:-fallbacken faktiskt
        // stryper. Inget auth-header sätts → partition på 127.0.0.1-IP-bucketen.
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClient();

        var statusCodes = new List<HttpStatusCode>();
        HttpResponseMessage? rejected = null;

        for (var i = 0; i < 10; i++)
        {
            var response = await client.PostAsJsonAsync(
                "/api/v1/me/job-ad-status",
                new { jobAdIds = Array.Empty<Guid>() },
                ct);
            statusCodes.Add(response.StatusCode);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rejected = response;
                break;
            }
        }

        statusCodes[0].ShouldBe(HttpStatusCode.OK,
            "första anonyma anropet ska vara 200 (anonym-tolerant, tom DTO) inom PermitLimit");
        statusCodes.ShouldContain(HttpStatusCode.TooManyRequests,
            "job-ad-status-batch-policy ip:-fallback ska strypa anonym DoS (TD-87)");
        rejected.ShouldNotBeNull("429 ska triggas inom 10 anrop vid PermitLimit=3");
        rejected.Headers.RetryAfter.ShouldNotBeNull(
            "RFC 6585: 429-respons ska inkludera Retry-After-header");
    }

    [Fact]
    public async Task POST_job_ad_status_with_auth_repeated_requests_returns_429_with_RetryAfter()
    {
        // Bevisar att DUAL-partitionen löser sig till user:-bucketen när "sub"
        // finns. Egen registrerad user → unik user:-bucket. OBS: det anonyma
        // testet ovan har redan förbrukat ip:-bucketen för samma policy, men ett
        // autentiserat anrop partitioneras på UserId (user:-prefix), INTE på IP →
        // de två bucketarna är oberoende. Därför är denna Fact säker även om det
        // anonyma testet körde först inom samma 60s-fönster.
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClient();

        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(client, ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

        var statusCodes = new List<HttpStatusCode>();
        HttpResponseMessage? rejected = null;

        for (var i = 0; i < 10; i++)
        {
            var response = await client.PostAsJsonAsync(
                "/api/v1/me/job-ad-status",
                new { jobAdIds = Array.Empty<Guid>() },
                ct);
            statusCodes.Add(response.StatusCode);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rejected = response;
                break;
            }
        }

        statusCodes[0].ShouldBe(HttpStatusCode.OK,
            "första autentiserade anropet ska vara 200 inom PermitLimit (user:-bucket)");
        statusCodes.ShouldContain(HttpStatusCode.TooManyRequests,
            "job-ad-status-batch-policy user:-bucket ska strypa efter PermitLimit");
        rejected.ShouldNotBeNull("429 ska triggas inom 10 anrop vid PermitLimit=3");
        rejected.Headers.RetryAfter.ShouldNotBeNull(
            "RFC 6585: 429-respons ska inkludera Retry-After-header");
    }

    [Fact]
    public async Task POST_saved_job_ads_with_auth_repeated_requests_returns_429_with_RetryAfter()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClient();

        // Egen user → unik UserId-partition (MeWrite delar inte budget med övriga).
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(client, ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

        var statusCodes = new List<HttpStatusCode>();
        HttpResponseMessage? rejected = null;

        // Godtyckligt jobAdId — annonsen finns inte → 404 (eller 204/409) är giltigt
        // som icke-429 första-respons. Det enda som spec:as är att MeWrite-policyn
        // stryper efter PermitLimit, inte resultatet av själva save-kommandot.
        var jobAdId = Guid.NewGuid();

        for (var i = 0; i < 10; i++)
        {
            var response = await client.PostAsync($"/api/v1/me/saved-job-ads/{jobAdId}", content: null, ct);
            statusCodes.Add(response.StatusCode);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rejected = response;
                break;
            }
        }

        statusCodes[0].ShouldNotBe(HttpStatusCode.TooManyRequests,
            "första anropet ska vara inom PermitLimit (204/404/409), inte 429");
        statusCodes.ShouldContain(HttpStatusCode.TooManyRequests,
            "me-write-policy ska strypa skriv-DoS efter PermitLimit");
        rejected.ShouldNotBeNull("429 ska triggas inom 10 anrop vid PermitLimit=3");
        rejected.Headers.RetryAfter.ShouldNotBeNull(
            "RFC 6585: 429-respons ska inkludera Retry-After-header");
    }
}
