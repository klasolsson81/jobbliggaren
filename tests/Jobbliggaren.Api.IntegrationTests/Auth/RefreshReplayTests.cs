using System.Net;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// /auth/refresh returnerar 410 Gone sedan Turn 4 (ADR 0017).
/// Refresh-flödet är ersatt av session-baserad autentisering.
/// Testet raderas i Fas 1 tillsammans med RefreshTokenStore och övrig JWT-infrastruktur.
/// </summary>
[Collection("Api")]
public class RefreshReplayTests(ApiFactory factory)
{
    [Fact]
    public async Task POST_refresh_returns_410_Gone()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClient();

        var response = await client.PostAsync("/api/v1/auth/refresh", content: null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Gone,
            "/auth/refresh är deprecated sedan ADR 0017 Turn 4 och ska returnera 410 Gone.");
    }
}
