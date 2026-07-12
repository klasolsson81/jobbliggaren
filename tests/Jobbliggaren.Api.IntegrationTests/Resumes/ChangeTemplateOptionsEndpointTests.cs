using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Resumes;

// Fas 4b PR-8b 8b.2 (ADR 0096) — HTTP wiring for PUT /resumes/{id}/template-options.
// Proves auth (401), the 204 happy path with persisted round-trip through the detail DTO,
// the composed persisted EffectiveAtsSafe verdict on both the detail and list DTOs, a mapped
// 400 for an invalid SmartEnum name, and the fail-closed cross-user IDOR (404, no oracle).
// Deep handler/validator semantics (photo-preservation, no-op-without-event) are unit-tested.
[Collection("Api")]
public class ChangeTemplateOptionsEndpointTests(ApiFactory factory)
{
    private static readonly object CreateBody = new { name = "Mitt CV", fullName = "Anna A" };

    private static object OptionsBody(
        string template = "MorkPanel", string accentColor = "ForestGreen",
        string fontPair = "Classic", string density = "Compact") =>
        new { template, accentColor, fontPair, density };

    private async Task<HttpClient> NewAuthedClientAsync(string prefix, CancellationToken ct)
    {
        var client = factory.CreateClient();
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            client, email: $"{prefix}-{Guid.NewGuid():N}@jobbliggaren.test", ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return client;
    }

    private static async Task<string> CreateResumeAsync(HttpClient client, CancellationToken ct)
    {
        var response = await client.PostAsJsonAsync("/api/v1/resumes", CreateBody, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task PUT_template_options_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClient();
        var response = await client.PutAsJsonAsync(
            $"/api/v1/resumes/{Guid.NewGuid()}/template-options", OptionsBody(), ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PUT_valid_returns_204_and_persists_options_on_detail()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await NewAuthedClientAsync("tpl", ct);
        var resumeId = await CreateResumeAsync(client, ct);

        var put = await client.PutAsJsonAsync(
            $"/api/v1/resumes/{resumeId}/template-options", OptionsBody(), ct);
        put.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var detail = await client.GetAsync($"/api/v1/resumes/{resumeId}", ct);
        detail.StatusCode.ShouldBe(HttpStatusCode.OK);
        var options = (await detail.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("templateOptions");

        options.GetProperty("template").GetString().ShouldBe("MorkPanel");
        options.GetProperty("accentColor").GetString().ShouldBe("ForestGreen");
        options.GetProperty("fontPair").GetString().ShouldBe("Classic");
        options.GetProperty("density").GetString().ShouldBe("Compact");
        // MorkPanel is two-column → not ATS-safe; photo stays off (preserved default).
        options.GetProperty("photoEnabled").GetBoolean().ShouldBeFalse();
        options.GetProperty("effectiveAtsSafe").GetBoolean().ShouldBeFalse();

        // The lean list DTO carries the same persisted false verdict for the card badge
        // (the not-ATS-safe case on the list layer, complementing the true case below).
        var list = await client.GetAsync("/api/v1/resumes", ct);
        var item = (await list.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("items").EnumerateArray()
            .Single(r => r.GetProperty("id").GetString() == resumeId);
        item.GetProperty("template").GetString().ShouldBe("MorkPanel");
        item.GetProperty("effectiveAtsSafe").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task PUT_ats_safe_template_reports_effectiveAtsSafe_true_on_detail_and_list()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await NewAuthedClientAsync("tpl", ct);
        var resumeId = await CreateResumeAsync(client, ct);

        var put = await client.PutAsJsonAsync(
            $"/api/v1/resumes/{resumeId}/template-options",
            OptionsBody(template: "Accentlinje", accentColor: "WineRed", fontPair: "Modern", density: "Airy"),
            ct);
        put.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var detail = await client.GetAsync($"/api/v1/resumes/{resumeId}", ct);
        (await detail.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("templateOptions").GetProperty("effectiveAtsSafe").GetBoolean()
            .ShouldBeTrue();

        // The lean list DTO carries the persisted verdict for the card badge.
        var list = await client.GetAsync("/api/v1/resumes", ct);
        var item = (await list.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("items").EnumerateArray()
            .Single(r => r.GetProperty("id").GetString() == resumeId);
        item.GetProperty("template").GetString().ShouldBe("Accentlinje");
        item.GetProperty("effectiveAtsSafe").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task PUT_invalid_template_name_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await NewAuthedClientAsync("tpl", ct);
        var resumeId = await CreateResumeAsync(client, ct);

        var put = await client.PutAsJsonAsync(
            $"/api/v1/resumes/{resumeId}/template-options", OptionsBody(template: "Bogus"), ct);
        put.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PUT_template_options_on_other_users_resume_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientA = await NewAuthedClientAsync("tpl-a", ct);
        var resumeAId = await CreateResumeAsync(clientA, ct);

        var clientB = await NewAuthedClientAsync("tpl-b", ct);
        var put = await clientB.PutAsJsonAsync(
            $"/api/v1/resumes/{resumeAId}/template-options", OptionsBody(), ct);

        // IDOR fail-closed: indistinguishable from unknown.
        put.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // A:s resume is untouched — still the ATS-safe default (Klar).
        var detailA = await clientA.GetAsync($"/api/v1/resumes/{resumeAId}", ct);
        (await detailA.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("templateOptions").GetProperty("template").GetString()
            .ShouldBe("Klar");
    }
}
