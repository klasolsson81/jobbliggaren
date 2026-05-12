using System.Net;
using System.Net.Http.Json;
using JobbPilot.Api.IntegrationTests.Helpers;
using JobbPilot.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace JobbPilot.Api.IntegrationTests.FeatureFlags;

/// <summary>
/// Verifierar att kill-switch (IFeatureFlags.RegistrationsOpen=false)
/// blockerar både invitation-redemption och waitlist-signup med 503.
/// Per ADR 0005 amendment 2026-05-12.
/// </summary>
[Collection("Api")]
public class RegistrationsClosedGateTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    [Fact]
    public async Task POST_waitlist_with_registrations_closed_returns_503()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.WithFeatureFlags(registrationsOpen: false).CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/waitlist/",
            new { email = $"closed-{Guid.NewGuid()}@example.com" },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task POST_redeem_invitation_with_registrations_closed_returns_503()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.WithFeatureFlags(registrationsOpen: false).CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/redeem-invitation",
            new { token = "any-token", password = AuthTestHelpers.DefaultTestPassword, displayName = "Test" },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }
}
