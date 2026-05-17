using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using JobbPilot.Api.IntegrationTests.Helpers;
using JobbPilot.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace JobbPilot.Api.IntegrationTests.Applications;

/// <summary>
/// TD-12 — verifiera cross-user-isolation för Application-aggregatet.
/// Queries och commands filtrerar på <c>a.JobSeekerId == jobSeekerId</c>;
/// detta test bevakar att user B inte kan se eller mutera user A:s
/// ansökningar.
///
/// Förväntat utfall: alla cross-user-anrop returnerar 404 (inte 403)
/// så att existens av annan users data inte avslöjas — same-pattern som
/// "unknown id"-respons.
/// </summary>
[Collection("Api")]
public class ApplicationsCrossUserIsolationTests(ApiFactory factory)
{
    private static readonly object CreateBody = new { jobAdId = (Guid?)null, coverLetter = (string?)null };

    private async Task<HttpClient> RegisterUserAsync(string userPrefix, CancellationToken ct)
    {
        var client = factory.CreateClient();
        var email = $"{userPrefix}-{Guid.NewGuid()}@example.com";
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(client, email, ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return client;
    }

    private static async Task<string> CreateApplicationAsync(HttpClient client, CancellationToken ct)
    {
        var response = await client.PostAsJsonAsync("/api/v1/applications", CreateBody, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return json.GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task User_B_GET_application_owned_by_user_A_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientA = await RegisterUserAsync("isolation-a", ct);
        var clientB = await RegisterUserAsync("isolation-b", ct);

        var applicationId = await CreateApplicationAsync(clientA, ct);

        var response = await clientB.GetAsync($"/api/v1/applications/{applicationId}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task User_B_POST_transition_on_user_A_application_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientA = await RegisterUserAsync("isolation-a", ct);
        var clientB = await RegisterUserAsync("isolation-b", ct);

        var applicationId = await CreateApplicationAsync(clientA, ct);

        var response = await clientB.PostAsJsonAsync(
            $"/api/v1/applications/{applicationId}/transition",
            new { targetStatus = "Submitted" },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task User_B_GET_applications_list_does_not_include_user_A_applications()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientA = await RegisterUserAsync("isolation-a", ct);
        var clientB = await RegisterUserAsync("isolation-b", ct);

        var applicationAId = await CreateApplicationAsync(clientA, ct);

        var response = await clientB.GetAsync("/api/v1/applications", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var items = json.GetProperty("items");
        items.ValueKind.ShouldBe(JsonValueKind.Array);
        items.EnumerateArray()
            .Any(a => a.GetProperty("id").GetString() == applicationAId)
            .ShouldBeFalse("user B:s list ska inte innehålla user A:s ansökan");
    }

    [Fact]
    public async Task User_B_GET_pipeline_does_not_include_user_A_applications()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientA = await RegisterUserAsync("isolation-a", ct);
        var clientB = await RegisterUserAsync("isolation-b", ct);

        await CreateApplicationAsync(clientA, ct);

        // User B har inga egna ansökningar → pipeline ska vara helt tom.
        // Strikt array-length-assert fångar även buggar där A:s ansökningar
        // skulle bubbla upp under andra status-namn än Draft.
        var response = await clientB.GetAsync("/api/v1/applications/pipeline", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.ValueKind.ShouldBe(JsonValueKind.Array);
        json.GetArrayLength().ShouldBe(0,
            "user B:s pipeline ska vara tom när bara A har ansökningar");
    }

    [Fact]
    public async Task User_B_POST_follow_up_on_user_A_application_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientA = await RegisterUserAsync("isolation-a", ct);
        var clientB = await RegisterUserAsync("isolation-b", ct);

        var applicationId = await CreateApplicationAsync(clientA, ct);

        var response = await clientB.PostAsJsonAsync(
            $"/api/v1/applications/{applicationId}/follow-ups",
            new
            {
                channel = "Email",
                scheduledAt = DateTimeOffset.UtcNow.AddDays(7).ToString("O"),
                note = (string?)null
            },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task User_B_POST_follow_up_outcome_on_user_A_application_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientA = await RegisterUserAsync("isolation-a", ct);
        var clientB = await RegisterUserAsync("isolation-b", ct);

        var applicationId = await CreateApplicationAsync(clientA, ct);

        var followUpResponse = await clientA.PostAsJsonAsync(
            $"/api/v1/applications/{applicationId}/follow-ups",
            new
            {
                channel = "Email",
                scheduledAt = DateTimeOffset.UtcNow.AddDays(7).ToString("O"),
                note = (string?)null
            },
            ct);
        followUpResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var followUpJson = await followUpResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var followUpId = followUpJson.GetProperty("id").GetString()!;

        var response = await clientB.PostAsJsonAsync(
            $"/api/v1/applications/{applicationId}/follow-ups/{followUpId}/outcome",
            new { outcome = "Responded" },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task User_B_POST_note_on_user_A_application_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientA = await RegisterUserAsync("isolation-a", ct);
        var clientB = await RegisterUserAsync("isolation-b", ct);

        var applicationId = await CreateApplicationAsync(clientA, ct);

        var response = await clientB.PostAsJsonAsync(
            $"/api/v1/applications/{applicationId}/notes",
            new { content = "User B should not be able to write here." },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task User_A_data_intact_after_user_B_attempted_cross_access()
    {
        // Säkerhetsinvariant: cross-user-attack ska inte ha sidoeffekter
        // på user A:s data — varken metadata-ändring eller cascading.
        var ct = TestContext.Current.CancellationToken;
        var clientA = await RegisterUserAsync("isolation-a", ct);
        var clientB = await RegisterUserAsync("isolation-b", ct);

        var applicationId = await CreateApplicationAsync(clientA, ct);

        // User B attempts attack-yta:
        await clientB.PostAsJsonAsync(
            $"/api/v1/applications/{applicationId}/transition",
            new { targetStatus = "Submitted" },
            ct);
        await clientB.PostAsJsonAsync(
            $"/api/v1/applications/{applicationId}/follow-ups",
            new
            {
                channel = "Email",
                scheduledAt = DateTimeOffset.UtcNow.AddDays(1).ToString("O"),
                note = (string?)null
            },
            ct);
        await clientB.PostAsJsonAsync(
            $"/api/v1/applications/{applicationId}/notes",
            new { content = "B's note" },
            ct);

        // User A:s data ska vara orörd: status Draft, inga follow-ups, inga notes.
        var aResponse = await clientA.GetAsync($"/api/v1/applications/{applicationId}", ct);
        aResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var aJson = await aResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        aJson.GetProperty("status").GetString().ShouldBe("Draft");
        aJson.GetProperty("followUps").GetArrayLength().ShouldBe(0);
        aJson.GetProperty("notes").GetArrayLength().ShouldBe(0);
    }
}
