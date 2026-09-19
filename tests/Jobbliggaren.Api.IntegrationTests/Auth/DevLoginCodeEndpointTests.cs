using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Auth;

/// <summary>
/// DEV-ONLY — <c>POST /api/v1/dev/login-code</c> (#1735, ADR 0142 D10), the positive counterpart to
/// <c>ProductionStartupSmokeTests.POST_dev_login_code_is_unmapped_in_Production_env</c>. ApiFactory runs as
/// Development and wraps its recording sender with the capture, so both gates are open here. The capturing
/// arm needs a RESERVED recipient: <c>@example.se</c> is a real domain, so a test using only that would
/// exercise nothing but the 404 (security-auditor Q15 condition 3).
/// </summary>
[Collection("Api")]
public class DevLoginCodeEndpointTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> RequestChallengeAsync(string email)
    {
        var before = _factory.Emails.LoginChallenges.Count(m => m.ToEmail == email);
        var response = await _client.PostAsJsonAsync("/api/v1/auth/challenge", new { email }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (_factory.Emails.LoginChallenges.Count(m => m.ToEmail == email) == before)
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, "the dispatch consumer never sent the challenge mail");
            await Task.Delay(25, Ct);
        }

        return body.RootElement.GetProperty("challengeId").GetString()!;
    }

    private Task<HttpResponseMessage> TakeCodeAsync(string email) =>
        _client.PostAsJsonAsync("/api/v1/dev/login-code", new { email }, Ct);

    [Fact]
    public async Task A_reserved_address_gets_its_code_once_and_the_code_signs_in()
    {
        var email = $"dev-code-{Guid.NewGuid():N}@example.com";
        await AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, email, ct: Ct);
        var challengeId = await RequestChallengeAsync(email);

        var taken = await TakeCodeAsync(email);

        taken.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await taken.Content.ReadAsStringAsync(Ct));
        var code = body.RootElement.GetProperty("code").GetString()!;
        (await _client.PostAsJsonAsync("/api/v1/auth/challenge/verify", new { challengeId, code }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TakeCodeAsync(email)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_real_domain_is_never_captured()
    {
        var email = $"dev-code-{Guid.NewGuid():N}@example.se";
        await AuthTestHelpers.RegisterAndGetSessionIdAsync(_factory, email, ct: Ct);
        await RequestChallengeAsync(email);

        (await TakeCodeAsync(email)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_blank_address_is_a_400()
    {
        (await TakeCodeAsync(" ")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_address_without_an_account_has_no_code_to_take()
    {
        var email = $"dev-code-{Guid.NewGuid():N}@example.com";
        await RequestChallengeAsync(email);

        (await TakeCodeAsync(email)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
