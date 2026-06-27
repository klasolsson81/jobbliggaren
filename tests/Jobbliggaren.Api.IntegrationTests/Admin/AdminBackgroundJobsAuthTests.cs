using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Admin;

/// <summary>
/// Auth-gate-tester för operatörsytan GET /api/v1/admin/jobs/{recurring,failed}
/// (#204 / TD-83). Bevisar de två yttre lagren av security-auditors must-clear
/// auth-gate: 401 utan auth-header, 403 för autentiserad icke-Admin. Båda nekas
/// av <c>RequireAuthorization(Admin)</c> INNAN handlern rör Hangfire-storage, så
/// dessa tester behöver inte ett bootstrappat hangfire-schema.
///
/// 200-vägen integrationstestas medvetet inte: ApiFactory bootstrappar inte
/// hangfire-schemat (Api kör <c>PrepareSchemaIfNecessary=false</c>; Worker äger
/// schema-bootstrap) och repo-konventionen integrationstestar inte Hangfire-
/// storage självt (jfr WorkerTestFixture: "Hangfire självt testas av Hangfire-
/// projektets tester"). Projektionen — den faktiska risk-/säkerhetsytan — täcks
/// av <see cref="AdminBackgroundJobsProjectionTests"/>; 200-vägen verifieras
/// manuellt i dev mot den körande stacken (DoD §4).
/// </summary>
[Collection("Api")]
public class AdminBackgroundJobsAuthTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    public static TheoryData<string> AdminJobsEndpoints =>
    [
        "/api/v1/admin/jobs/recurring",
        "/api/v1/admin/jobs/failed",
    ];

    [Theory]
    [MemberData(nameof(AdminJobsEndpoints))]
    public async Task Get_WithoutAuth_Returns401(string url)
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();

        var response = await client.GetAsync(url, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(AdminJobsEndpoints))]
    public async Task Get_WithAuthenticatedNonAdmin_Returns403(string url)
    {
        var ct = TestContext.Current.CancellationToken;
        var client = await RegisterNonAdminClientAsync(ct);

        var response = await client.GetAsync(url, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private async Task<HttpClient> RegisterNonAdminClientAsync(CancellationToken ct)
    {
        var client = _factory.CreateClient();
        var email = $"admin-jobs-{Guid.NewGuid():N}@jobbliggaren.test";
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(client, email, ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

        // Bekräfta att klienten faktiskt är autentiserad (icke-Admin) så att 403
        // är roll-nekan, inte ett missat auth-header (skulle annars ge 401).
        var me = await client.GetAsync("/api/v1/me", ct);
        me.EnsureSuccessStatusCode();
        var meJson = await me.Content.ReadFromJsonAsync<JsonElement>(ct);
        meJson.GetProperty("userId").GetString().ShouldNotBeNullOrEmpty();

        return client;
    }
}
