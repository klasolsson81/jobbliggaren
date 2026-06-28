using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Applications;

// #316 — AF-aktivitetsrapport endpoint GET /api/v1/applications/activity-report.
// Verifierar auth-gate, validerings-400 på missformat par, samt cross-user-
// isolation END-TO-END via riktig Postgres (Testcontainers). Speglar
// ApplicationsTests/ApplicationsCrossUserIsolationTests (ApiFactory, session-
// bearer-auth, [Collection("Api")]). Location-resolvering (shadow-prop ur
// raw_payload) täcks i GetActivityReportLocationIntegrationTests (handler-nivå)
// eftersom POST-vägen inte sätter raw_payload.
[Collection("Api")]
public class GetActivityReportTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly object CreateBody = new { jobAdId = (Guid?)null, coverLetter = (string?)null };

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(_client, ct: ct);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
    }

    private async Task<HttpClient> RegisterUserAsync(string prefix, CancellationToken ct)
    {
        var client = factory.CreateClient();
        var email = $"{prefix}-{Guid.NewGuid()}@example.com";
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(client, email, ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return client;
    }

    // Submittar en (just nu) Draft-ansökan → stämplar AppliedAt = "nu" (server-
    // klocka). Returnerar applikations-id.
    private static async Task<string> CreateSubmittedApplicationAsync(HttpClient client, CancellationToken ct)
    {
        var postResponse = await client.PostAsJsonAsync("/api/v1/applications", CreateBody, ct);
        postResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await postResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;

        var transition = await client.PostAsJsonAsync(
            $"/api/v1/applications/{id}/transition", new { targetStatus = "Submitted" }, ct);
        transition.StatusCode.ShouldBe(HttpStatusCode.OK);
        return id;
    }

    // Aktuell-månad-fönstret som server-klockan stämplar AppliedAt i.
    private static (int Year, int Month) CurrentMonth()
    {
        var now = DateTimeOffset.UtcNow;
        return (now.Year, now.Month);
    }

    // ---------------------------------------------------------------
    // Auth-gate
    // ---------------------------------------------------------------

    [Fact]
    public async Task GET_activity_report_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.GetAsync("/api/v1/applications/activity-report", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_activity_report_with_auth_returns_200_with_echoed_year_month()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.GetAsync(
            "/api/v1/applications/activity-report?year=2026&month=6", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("year").GetInt32().ShouldBe(2026);
        json.GetProperty("month").GetInt32().ShouldBe(6);
        json.GetProperty("applications").ValueKind.ShouldBe(JsonValueKind.Array);
    }

    // ---------------------------------------------------------------
    // Validering — missformat par → 400
    // ---------------------------------------------------------------

    [Fact]
    public async Task GET_activity_report_with_year_but_no_month_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.GetAsync("/api/v1/applications/activity-report?year=2026", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GET_activity_report_with_month_out_of_range_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var response = await _client.GetAsync(
            "/api/v1/applications/activity-report?year=2026&month=99", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ---------------------------------------------------------------
    // Innehåll — egna submittade ansökningar i månaden
    // ---------------------------------------------------------------

    [Fact]
    public async Task GET_activity_report_returns_own_submitted_application_for_current_month()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        var id = await CreateSubmittedApplicationAsync(_client, ct);
        var (year, month) = CurrentMonth();

        var response = await _client.GetAsync(
            $"/api/v1/applications/activity-report?year={year}&month={month}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var applications = json.GetProperty("applications");
        applications.EnumerateArray()
            .Any(a => a.GetProperty("applicationId").GetString() == id)
            .ShouldBeTrue("egen submittad ansökan ska finnas i innevarande månads rapport");
    }

    [Fact]
    public async Task GET_activity_report_excludes_draft_application()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        // Skapa men submitta ALDRIG → Draft → AppliedAt null → exkluderad.
        var postResponse = await _client.PostAsJsonAsync("/api/v1/applications", CreateBody, ct);
        var draftId = (await postResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;
        var (year, month) = CurrentMonth();

        var response = await _client.GetAsync(
            $"/api/v1/applications/activity-report?year={year}&month={month}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("applications").EnumerateArray()
            .Any(a => a.GetProperty("applicationId").GetString() == draftId)
            .ShouldBeFalse("Draft-ansökan (utan AppliedAt) ska inte finnas i rapporten");
    }

    // ---------------------------------------------------------------
    // Cross-user-isolation
    // ---------------------------------------------------------------

    [Fact]
    public async Task GET_activity_report_does_not_return_other_users_applications()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientA = await RegisterUserAsync("activity-a", ct);
        var clientB = await RegisterUserAsync("activity-b", ct);

        var aId = await CreateSubmittedApplicationAsync(clientA, ct);
        var (year, month) = CurrentMonth();

        var response = await clientB.GetAsync(
            $"/api/v1/applications/activity-report?year={year}&month={month}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("applications").EnumerateArray()
            .Any(a => a.GetProperty("applicationId").GetString() == aId)
            .ShouldBeFalse("user B:s rapport ska inte innehålla user A:s ansökan");
    }
}
