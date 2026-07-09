using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Auth;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

[Collection("Api")]
public class RegisterTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task POST_register_with_valid_data_returns_session_id()
    {
        var ct = TestContext.Current.CancellationToken;
        var body = new
        {
            email = $"reg-{Guid.NewGuid()}@example.com",
            password = "T3stlosen123456",
            displayName = "Test User",
        };

        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", body, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("sessionId").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task POST_register_with_duplicate_email_returns_400()
    {
        // #481 Low — a duplicate registration must not leak account existence. The 400 is collapsed
        // to a generic Auth.DuplicateAccount whose message names NEITHER the field NOR the submitted
        // address (vs Identity's raw English "Username 'x@y.z' is already taken", which echoed the
        // email). The residual 200-vs-400 status oracle inherent to instant-login registration is
        // deferred by design (closing it needs email-confirmation-first registration) and is
        // deliberately NOT asserted here — this pins only the message/code normalization.
        var ct = TestContext.Current.CancellationToken;
        var email = $"dup-{Guid.NewGuid()}@example.com";
        var body = new { email, password = "T3stlosen123456", displayName = "First User" };

        await _client.PostAsJsonAsync("/api/v1/auth/register", body, ct);
        var second = await _client.PostAsJsonAsync("/api/v1/auth/register", body, ct);

        second.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await second.Content.ReadFromJsonAsync<JsonElement>(ct);
        var title = problem.GetProperty("title").GetString();
        var detail = problem.GetProperty("detail").GetString();

        // Generic, non-enumerating code + message, single-sourced from AuthErrorCodes so the wire copy
        // cannot silently drift. title == Auth.DuplicateAccount also proves the raw Identity code
        // (DuplicateUserName / DuplicateEmail) never surfaced as "Auth.{code}".
        title.ShouldBe(AuthErrorCodes.DuplicateAccount);
        detail.ShouldBe(AuthErrorCodes.DuplicateAccountMessage);

        // The two enumeration guards, independent of the constants above: the response body must echo
        // neither the submitted address nor Identity's raw English "is already taken" wording. The
        // '!' is honest — the ShouldBe above already fails the test if detail is null.
        detail!.ShouldNotContain(email);
        detail!.ShouldNotContain("is already taken");
    }

    [Fact]
    public async Task POST_register_with_blank_display_name_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        var body = new
        {
            email = $"blank-{Guid.NewGuid()}@example.com",
            password = "T3stlosen123456",
            displayName = "   ",
        };

        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", body, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
