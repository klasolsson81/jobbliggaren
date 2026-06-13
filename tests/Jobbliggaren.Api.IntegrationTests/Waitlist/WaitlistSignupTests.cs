using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Waitlist;

[Collection("Api")]
public class WaitlistSignupTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private const string ValidName = "Testperson Integration";
    private const string ValidMotivation =
        "Integration-test seed med tillräckligt lång motivering för validering.";

    private static object ValidPayload(string email, bool marketing = false) => new
    {
        email,
        name = ValidName,
        motivation = ValidMotivation,
        marketingEmailAccepted = marketing,
    };

    [Fact]
    public async Task POST_waitlist_with_valid_payload_returns_200_with_entry_id()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"wait-{Guid.NewGuid()}@example.com";

        var response = await _client.PostAsJsonAsync(
            "/api/v1/waitlist/", ValidPayload(email), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("waitlistEntryId").GetGuid().ShouldNotBe(Guid.Empty);
        json.GetProperty("email").GetString().ShouldBe(email);
    }

    [Fact]
    public async Task POST_waitlist_duplicate_pending_email_returns_same_entry_idempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        var email = $"dup-wait-{Guid.NewGuid()}@example.com";

        var first = await _client.PostAsJsonAsync("/api/v1/waitlist/", ValidPayload(email), ct);
        var second = await _client.PostAsJsonAsync("/api/v1/waitlist/", ValidPayload(email), ct);

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);

        var firstId = (await first.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("waitlistEntryId").GetGuid();
        var secondId = (await second.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("waitlistEntryId").GetGuid();

        secondId.ShouldBe(firstId);
    }

    [Fact]
    public async Task POST_waitlist_with_invalid_email_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.PostAsJsonAsync(
            "/api/v1/waitlist/",
            new
            {
                email = "no-at-sign",
                name = ValidName,
                motivation = ValidMotivation,
                marketingEmailAccepted = false,
            },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
