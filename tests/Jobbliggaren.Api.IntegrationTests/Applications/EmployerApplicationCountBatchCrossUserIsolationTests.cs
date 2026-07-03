using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Applications;

/// <summary>
/// #446 (#311; ADR 0090 D1 M2, GDPR / IDOR) — CROSS-USER isolation of the /jobb card count overlay
/// (<c>POST /api/v1/me/application-history/counts</c>). The count is owner-scoped on <c>JobSeekerId</c>
/// resolved from <c>ICurrentUser</c> (never the wire), so one user's badge can never aggregate another
/// user's applications. Parity <see cref="ApplicationHistoryCrossUserIsolationTests"/>: two registered
/// users, Bearer session auth, the shared [Collection("Api")] Postgres is never reset. The request
/// carries only JobAdIds and the response only <c>int</c> counts — no org.nr in either direction.
/// </summary>
[Collection("Api")]
public class EmployerApplicationCountBatchCrossUserIsolationTests(ApiFactory factory)
{
    private const string CountsEndpoint = "/api/v1/me/application-history/counts";

    // 3rd digit >= 2 → legal-entity-shaped (never personnummer-masked); org.nr never surfaces anyway.
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

        var externalId = $"eacb-iso-{Guid.NewGuid():N}";
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
            publishedAt: clock.UtcNow.AddDays(-1),
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock).Value;

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd.Id.Value;
    }

    // POST /from-job-ad creates AND immediately transitions to Submitted (stamping AppliedAt) — history
    // the moment it is created (parity ApplicationHistoryCrossUserIsolationTests).
    private static async Task ApplyFromJobAdAsync(HttpClient client, Guid jobAdId, CancellationToken ct)
    {
        var create = await client.PostAsync($"/api/v1/applications/from-job-ad/{jobAdId}", content: null, ct);
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    private static async Task<JsonElement> CountsAsync(HttpClient client, Guid[] jobAdIds, CancellationToken ct)
    {
        var response = await client.PostAsJsonAsync(CountsEndpoint, new { jobAdIds }, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    private static int CountFor(JsonElement body, Guid jobAdId)
    {
        var map = body.GetProperty("countsByJobAdId");
        // Positive-only: an absent key means zero (the FE renders no badge).
        return map.TryGetProperty(jobAdId.ToString(), out var value) ? value.GetInt32() : 0;
    }

    [Fact]
    public async Task Anonymous_request_is_rejected_401()
    {
        var ct = TestContext.Current.CancellationToken;

        // No Authorization header → the transport RequireAuthorization() gate rejects before the handler
        // runs (application-history profiling is auth-gated, unlike the anonymous-tolerant status batch).
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            CountsEndpoint, new { jobAdIds = new[] { Guid.NewGuid() } }, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Two_users_applying_to_same_employer_each_see_only_their_own_count()
    {
        var ct = TestContext.Current.CancellationToken;

        var orgNr = NewOrgNr();
        // A prior ad both users apply to, and a DIFFERENT page ad of the same employer whose badge we read.
        var priorAdId = await SeedJobAdAsync(orgNr, ct);
        var pageAdId = await SeedJobAdAsync(orgNr, ct);

        var clientA = await RegisterUserAsync("eacb-iso-a", ct);
        var clientB = await RegisterUserAsync("eacb-iso-b", ct);

        // A applies ONCE; B applies TWICE — to the SAME employer.
        await ApplyFromJobAdAsync(clientA, priorAdId, ct);
        await ApplyFromJobAdAsync(clientB, priorAdId, ct);
        await ApplyFromJobAdAsync(clientB, pageAdId, ct);

        var countsA = await CountsAsync(clientA, [pageAdId], ct);
        var countsB = await CountsAsync(clientB, [pageAdId], ct);

        CountFor(countsA, pageAdId).ShouldBe(1,
            "A:s badge ska spegla A:s EGNA ansökan till arbetsgivaren (1) — aldrig aggregera B:s.");
        CountFor(countsB, pageAdId).ShouldBe(2,
            "B:s badge ska spegla B:s EGNA två ansökningar (2) — aldrig A:s. Räknaren är ägar-scopad " +
            "på JobSeekerId från ICurrentUser (ingen cross-user-JOIN, ADR 0090 D1 M2).");
    }

    [Fact]
    public async Task Page_ad_the_caller_never_applied_to_the_employer_of_is_absent()
    {
        var ct = TestContext.Current.CancellationToken;

        var orgNr = NewOrgNr();
        var pageAdId = await SeedJobAdAsync(orgNr, ct);

        var clientA = await RegisterUserAsync("eacb-iso-a2", ct);
        var clientB = await RegisterUserAsync("eacb-iso-b2", ct);

        // Only A applies to this employer. B applies to nothing.
        await ApplyFromJobAdAsync(clientA, pageAdId, ct);

        var countsB = await CountsAsync(clientB, [pageAdId], ct);

        CountFor(countsB, pageAdId).ShouldBe(0,
            "B får ALDRIG en badge för en arbetsgivare bara A ansökt till — räknaren är ägar-scopad.");
    }
}
