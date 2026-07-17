using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Resumes;

// Fas 4 STEG B / B3 — HTTP wiring for the STEG A PromoteParsedResume command. Imports an
// artifact, then promotes it with a user-approved gap-filled ResumeContentDto into a canonical
// Resume. Proves auth, the 201→new-Resume mapping, the DQ6 personnummer re-scan over the
// SUBMITTED content (→ 400), and the fail-closed IDOR. The deep promotion/encryption is already
// covered by the STEG A unit + Worker integration tests.
[Collection("Api")]
public class PromoteParsedResumeEndpointTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly byte[] PdfBytes = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37];

    private static async Task<HttpClient> NewAuthedClientAsync(ApiFactory f, CancellationToken ct)
    {
        var client = f.CreateClient();
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            client, email: $"promote-{Guid.NewGuid():N}@jobbliggaren.test", ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return client;
    }

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            _client, email: $"promote-{Guid.NewGuid():N}@jobbliggaren.test", ct: ct);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
    }

    private static async Task<string> ImportAsync(HttpClient client, CancellationToken ct)
    {
        var part = new ByteArrayContent(PdfBytes);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        using var form = new MultipartFormDataContent { { part, "file", "cv.pdf" } };
        var import = await client.PostAsync("/api/v1/resumes/import", form, ct);
        import.IsSuccessStatusCode.ShouldBeTrue();
        return (await import.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("parsedResumeId").GetString()!;
    }

    private static object PromoteBody(string name = "Mitt CV", string? summary = null) =>
        new
        {
            name,
            content = new
            {
                personalInfo = new { fullName = "Anna Andersson", email = "anna@example.se", phone = (string?)null, location = "Stockholm" },
                experiences = Array.Empty<object>(),
                educations = Array.Empty<object>(),
                skills = Array.Empty<object>(),
                summary,
            },
        };

    [Fact]
    public async Task POST_promote_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/resumes/parsed/{Guid.NewGuid()}/promote", PromoteBody(), ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_promote_unknown_id_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/resumes/parsed/{Guid.NewGuid()}/promote", PromoteBody(), ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Import_then_promote_returns_201_and_new_resume_appears_in_list()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var parsedId = await ImportAsync(_client, ct);

        var promote = await _client.PostAsJsonAsync(
            $"/api/v1/resumes/parsed/{parsedId}/promote", PromoteBody("Promoterat CV"), ct);

        promote.StatusCode.ShouldBe(HttpStatusCode.Created);
        var newResumeId = (await promote.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;
        Guid.Parse(newResumeId).ShouldNotBe(Guid.Empty);

        // The new canonical Resume is listed; the staging artifact is now gone (promoted).
        var list = await _client.GetAsync("/api/v1/resumes", ct);
        var items = (await list.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("items");
        var listed = items.EnumerateArray().Single(r => r.GetProperty("id").GetString() == newResumeId);

        // Fas 4b PR-8.1 (CTO-bind Q1): the promote reconciled the DEK-free ledger in the
        // same transaction, so the hub badge data is live from the first save — the list
        // item carries a NON-null openFindingCount (reviewed at the current rubric
        // version; the exact count is the engine's business) plus the ADR 0096 metadata.
        listed.GetProperty("openFindingCount").ValueKind.ShouldNotBe(JsonValueKind.Null);
        listed.GetProperty("origin").GetString().ShouldBe("Import");
        listed.GetProperty("template").GetString().ShouldBe("Klar");

        // Regression net for the translated correlated counts against REAL Postgres
        // (dotnet-architect PR-8.1 Major): a freshly promoted CV has exactly its Master
        // version — an in-memory count over the unloaded navigation would read 0 here.
        listed.GetProperty("versionCount").GetInt32().ShouldBe(1);

        var getParsed = await _client.GetAsync($"/api/v1/resumes/parsed/{parsedId}", ct);
        getParsed.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Promote_seeds_badge_count_and_a_resolved_decision_decrements_it()
    {
        // Fas 4b PR-8.1 (code-reviewer Minor 2): pin the badge VALUE end-to-end against
        // real Postgres — promote seeds N Open rows (the minimal body guarantees at
        // least one actionable finding), a user Resolved decision decrements the
        // translated count by exactly one.
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var parsedId = await ImportAsync(_client, ct);

        var promote = await _client.PostAsJsonAsync(
            $"/api/v1/resumes/parsed/{parsedId}/promote", PromoteBody("Badge-CV"), ct);
        promote.StatusCode.ShouldBe(HttpStatusCode.Created);
        var resumeId = (await promote.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;

        var openBefore = await OpenFindingCountAsync(_client, resumeId, ct);
        openBefore.ShouldBeGreaterThan(0);

        // Pick any actionable finding from the canonical review and resolve it.
        var review = await _client.GetAsync($"/api/v1/resumes/{resumeId}/review?profile=Ats", ct);
        review.StatusCode.ShouldBe(HttpStatusCode.OK);
        var criterionId = (await review.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("verdicts").EnumerateArray()
            .First(v => v.GetProperty("verdict").GetString() is "Fail" or "Warn")
            .GetProperty("criterionId").GetString()!;

        var put = await _client.PutAsJsonAsync(
            $"/api/v1/resumes/{resumeId}/review/findings/{criterionId}/status",
            new { status = "Resolved" }, ct);
        put.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await OpenFindingCountAsync(_client, resumeId, ct)).ShouldBe(openBefore - 1);
    }

    private static async Task<int> OpenFindingCountAsync(
        HttpClient client, string resumeId, CancellationToken ct)
    {
        var list = await client.GetAsync("/api/v1/resumes", ct);
        list.StatusCode.ShouldBe(HttpStatusCode.OK);
        var item = (await list.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("items").EnumerateArray()
            .Single(r => r.GetProperty("id").GetString() == resumeId);
        var count = item.GetProperty("openFindingCount");
        count.ValueKind.ShouldNotBe(JsonValueKind.Null);
        return count.GetInt32();
    }

    [Fact]
    public async Task Import_then_promote_with_personnummer_in_content_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var parsedId = await ImportAsync(_client, ct);

        // DQ6: the handler re-scans ALL submitted free text — a personnummer the user typed into
        // the gap-fill content (here the summary) is flagged before construction → 400.
        var promote = await _client.PostAsJsonAsync(
            $"/api/v1/resumes/parsed/{parsedId}/promote",
            PromoteBody(summary: "Mitt personnummer är 811218-9876."), ct);

        promote.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task POST_promote_with_empty_name_returns_400()
    {
        // Exercises the ValidationBehavior → ValidationException → middleware-400 path over HTTP
        // (the validator is otherwise only unit-tested in isolation).
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var parsedId = await ImportAsync(_client, ct);

        var promote = await _client.PostAsJsonAsync(
            $"/api/v1/resumes/parsed/{parsedId}/promote", PromoteBody(name: ""), ct);

        promote.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task POST_promote_belonging_to_other_user_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;

        var clientA = await NewAuthedClientAsync(_factory, ct);
        var parsedIdA = await ImportAsync(clientA, ct);

        var clientB = await NewAuthedClientAsync(_factory, ct);
        var promoteB = await clientB.PostAsJsonAsync(
            $"/api/v1/resumes/parsed/{parsedIdA}/promote", PromoteBody(), ct);

        promoteB.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
