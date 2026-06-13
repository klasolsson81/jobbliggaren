using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Resumes;

/// <summary>
/// TD-66 — cross-user-isolation för Resume-aggregatet. Speglar pattern från
/// <c>ApplicationsCrossUserIsolationTests</c>. Verifierar att user B inte
/// kan se eller mutera user A:s CV via någon endpoint.
///
/// Förväntat utfall: alla cross-user-anrop returnerar 404 (inte 403) så
/// existens av annan users data inte avslöjas.
/// </summary>
[Collection("Api")]
public class ResumesCrossUserIsolationTests(ApiFactory factory)
{
    private static readonly object CreateBody = new { name = "Mitt CV", fullName = "Anna A" };

    private static object MasterContentBody(string fullName = "Anna A") => new
    {
        personalInfo = new
        {
            fullName,
            email = "anna@example.se",
            phone = (string?)null,
            location = "Stockholm"
        },
        experiences = Array.Empty<object>(),
        educations = Array.Empty<object>(),
        skills = Array.Empty<object>(),
        summary = (string?)null
    };

    private async Task<HttpClient> RegisterUserAsync(string userPrefix, CancellationToken ct)
    {
        var client = factory.CreateClient();
        var email = $"{userPrefix}-{Guid.NewGuid()}@example.com";
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(client, email, ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return client;
    }

    private static async Task<string> CreateResumeAsync(HttpClient client, CancellationToken ct)
    {
        var response = await client.PostAsJsonAsync("/api/v1/resumes", CreateBody, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return json.GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task User_B_GET_resume_owned_by_user_A_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientA = await RegisterUserAsync("resume-iso-a", ct);
        var clientB = await RegisterUserAsync("resume-iso-b", ct);

        var resumeId = await CreateResumeAsync(clientA, ct);

        var response = await clientB.GetAsync($"/api/v1/resumes/{resumeId}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task User_B_PATCH_resume_owned_by_user_A_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientA = await RegisterUserAsync("resume-iso-a", ct);
        var clientB = await RegisterUserAsync("resume-iso-b", ct);

        var resumeId = await CreateResumeAsync(clientA, ct);

        var response = await clientB.PatchAsJsonAsync(
            $"/api/v1/resumes/{resumeId}",
            new { name = "Förändrat av B" },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task User_B_PUT_master_content_on_user_A_resume_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientA = await RegisterUserAsync("resume-iso-a", ct);
        var clientB = await RegisterUserAsync("resume-iso-b", ct);

        var resumeId = await CreateResumeAsync(clientA, ct);

        var response = await clientB.PutAsJsonAsync(
            $"/api/v1/resumes/{resumeId}/master",
            MasterContentBody("Bertil B"),
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task User_B_DELETE_version_on_user_A_resume_returns_404()
    {
        // Defense-in-depth: även om B inte kan läsa A:s version-id:n så
        // bevakar testet att enumeration-attack med slumpat versionId mot
        // A:s resumeId returnerar 404 (inte 403 eller 400 som skulle leak:a
        // existens-info).
        var ct = TestContext.Current.CancellationToken;
        var clientA = await RegisterUserAsync("resume-iso-a", ct);
        var clientB = await RegisterUserAsync("resume-iso-b", ct);

        var resumeId = await CreateResumeAsync(clientA, ct);
        var fakeVersionId = Guid.NewGuid();

        var response = await clientB.DeleteAsync(
            $"/api/v1/resumes/{resumeId}/versions/{fakeVersionId}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task User_B_DELETE_user_A_resume_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientA = await RegisterUserAsync("resume-iso-a", ct);
        var clientB = await RegisterUserAsync("resume-iso-b", ct);

        var resumeId = await CreateResumeAsync(clientA, ct);

        var response = await clientB.DeleteAsync($"/api/v1/resumes/{resumeId}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task User_B_GET_resumes_list_does_not_include_user_A_resumes()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientA = await RegisterUserAsync("resume-iso-a", ct);
        var clientB = await RegisterUserAsync("resume-iso-b", ct);

        var resumeAId = await CreateResumeAsync(clientA, ct);

        var response = await clientB.GetAsync("/api/v1/resumes", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.ValueKind.ShouldBe(JsonValueKind.Object);
        var items = json.GetProperty("items");
        items.ValueKind.ShouldBe(JsonValueKind.Array);
        items.EnumerateArray()
            .Any(r => r.GetProperty("id").GetString() == resumeAId)
            .ShouldBeFalse("user B:s lista ska inte innehålla user A:s CV");
    }

    [Fact]
    public async Task User_A_resume_intact_after_user_B_attempted_cross_access()
    {
        // Säkerhetsinvariant: cross-user-attacks ger inga sidoeffekter på A:s data.
        var ct = TestContext.Current.CancellationToken;
        var clientA = await RegisterUserAsync("resume-iso-a", ct);
        var clientB = await RegisterUserAsync("resume-iso-b", ct);

        var resumeId = await CreateResumeAsync(clientA, ct);

        // User B attempts attack-yta:
        await clientB.PatchAsJsonAsync(
            $"/api/v1/resumes/{resumeId}",
            new { name = "Förändrat av B" },
            ct);
        await clientB.PutAsJsonAsync(
            $"/api/v1/resumes/{resumeId}/master",
            MasterContentBody("Bertil B"),
            ct);
        await clientB.DeleteAsync($"/api/v1/resumes/{resumeId}", ct);

        // User A:s CV ska vara orörd: namn = original, ej raderad.
        var aResponse = await clientA.GetAsync($"/api/v1/resumes/{resumeId}", ct);
        aResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var aJson = await aResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        aJson.GetProperty("name").GetString().ShouldBe("Mitt CV");
    }
}
