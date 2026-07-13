using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Applications;

/// <summary>
/// #444 (ADR 0090 D1 M2, GDPR / IDOR) — CROSS-USER isolation of the employer application-history
/// endpoint (<c>GET /api/v1/me/application-history</c>). The projection is owner-scoped on
/// <c>JobSeekerId</c> resolved from <c>ICurrentUser</c> (never the wire), so one user's history can
/// never surface or aggregate another user's applications. Parity
/// <see cref="CompanyWatches.CompanyWatchesMatchCountCrossUserIsolationTests"/>: two registered users,
/// Bearer session auth, private org.nr (the shared [Collection("Api")] Postgres is never reset).
/// </summary>
[Collection("Api")]
public class ApplicationHistoryCrossUserIsolationTests(ApiFactory factory)
{
    private const string HistoryEndpoint = "/api/v1/me/application-history";

    // 3rd digit >= 2 → legal-entity-shaped (never personnummer-masked), so the org.nr round-trips in
    // the response and can be asserted on directly.
    private static string NewOrgNr() => $"556{Random.Shared.Next(1_000_000, 9_999_999)}";

    private async Task<HttpClient> RegisterUserAsync(string prefix, CancellationToken ct)
    {
        var client = factory.CreateClient();
        var email = $"{prefix}-{Guid.NewGuid()}@example.com";
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(client, email, ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return client;
    }

    private async Task<Guid> SeedJobAdAsync(string orgNr, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var externalId = $"ahiso-{Guid.NewGuid():N}";
        var rawPayload =
            $"{{\"id\":\"{externalId}\","
            + $"\"employer\":{{\"name\":\"Delad AB\",\"organization_number\":\"{orgNr}\"}}}}";

        var jobAd = JobAd.Import(
            title: "Delad annons",
            company: Company.Create("Delad AB").Value,
            description: "beskrivning",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            facets: TestFacets.FromPayload(rawPayload),
            publishedAt: clock.UtcNow.AddDays(-1),
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock).Value;

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd.Id.Value;
    }

    // POST /from-job-ad is the "Har ansökt" quick-create: it creates AND immediately transitions to
    // Submitted (stamping AppliedAt), so no explicit transition is needed — the application is history
    // the moment it is created.
    private static async Task ApplyFromJobAdAsync(HttpClient client, Guid jobAdId, CancellationToken ct)
    {
        var create = await client.PostAsync($"/api/v1/applications/from-job-ad/{jobAdId}", content: null, ct);
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    private static async Task<JsonElement> HistoryAsync(HttpClient client, CancellationToken ct)
    {
        var response = await client.GetAsync(HistoryEndpoint, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    [Fact]
    public async Task Anonymous_request_is_rejected_401()
    {
        var ct = TestContext.Current.CancellationToken;

        // No Authorization header -> the transport RequireAuthorization() gate rejects before the
        // handler runs (defense-in-depth beyond the IAuthenticatedRequest pipeline marker).
        var client = factory.CreateClient();

        var response = await client.GetAsync(HistoryEndpoint, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Two_users_applying_to_same_employer_each_see_only_their_own_count()
    {
        var ct = TestContext.Current.CancellationToken;

        var orgNr = NewOrgNr();
        var jobAdId = await SeedJobAdAsync(orgNr, ct);

        var clientA = await RegisterUserAsync("ah-iso-a", ct);
        var clientB = await RegisterUserAsync("ah-iso-b", ct);

        // Both users apply ONCE to the SAME shared employer.
        await ApplyFromJobAdAsync(clientA, jobAdId, ct);
        await ApplyFromJobAdAsync(clientB, jobAdId, ct);

        var historyA = await HistoryAsync(clientA, ct);
        var historyB = await HistoryAsync(clientB, ct);

        var rowA = historyA.EnumerateArray()
            .Single(e => e.GetProperty("organizationNumber").GetString() == orgNr);
        rowA.GetProperty("applicationCount").GetInt32().ShouldBe(1,
            "User A:s applicationCount ska spegla A:s EGNA ansökan (1) — aldrig aggregera B:s.");

        var rowB = historyB.EnumerateArray()
            .Single(e => e.GetProperty("organizationNumber").GetString() == orgNr);
        rowB.GetProperty("applicationCount").GetInt32().ShouldBe(1,
            "User B:s applicationCount ska spegla B:s EGNA ansökan (1) — aldrig A:s. Projektionen är " +
            "ägar-scopad på JobSeekerId från ICurrentUser (ingen cross-user-JOIN, ADR 0090 D1 M2).");
    }

    [Fact]
    public async Task User_B_history_never_contains_an_employer_only_user_A_applied_to()
    {
        var ct = TestContext.Current.CancellationToken;

        var orgNr = NewOrgNr();
        var jobAdId = await SeedJobAdAsync(orgNr, ct);

        var clientA = await RegisterUserAsync("ah-iso-a2", ct);
        var clientB = await RegisterUserAsync("ah-iso-b2", ct);

        // Only A applies. B applies to nothing.
        await ApplyFromJobAdAsync(clientA, jobAdId, ct);

        var historyB = await HistoryAsync(clientB, ct);

        historyB.EnumerateArray()
            .Any(e => e.GetProperty("organizationNumber").GetString() == orgNr)
            .ShouldBeFalse(
                "User B får ALDRIG se en arbetsgivare som bara A ansökt till — B:s historik är " +
                "ägar-scopad och innehåller enbart B:s egna ansökningar.");
    }
}
