using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.IntegrationTests.Sessions;
using Jobbliggaren.Application.Admin.JuniorJobAds.Queries.ListJuniorJobAds;
using Jobbliggaren.Application.JobAds.Jobs.ClassifyJuniorJobAds;
using Jobbliggaren.Domain.Auditing;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Admin;

/// <summary>
/// #2264 (ADR 0187) — the admin junior queue's two routes on the real host: the Admin policy on both, answered before
/// any handler runs; a private, unstored answer to an administrator; and a decision audited as
/// <c>Admin.JuniorDecisionRecorded</c> on the ad. The real rules, the real sweep and the shared database: other
/// classes' rows live there, so the exact tabs and counts are asserted in <c>AdminJuniorQueueHandlerTests</c>.
/// </summary>
/// <remarks>
/// Every ad is imported through <c>JobAd.Import</c> and classified by the host's own <c>ClassifyJuniorJobAdsJob</c>, as
/// the Worker's two-minute tick does; the shipped rule file calls this title and text a junior job, which
/// <c>PublicJuniorItJobAdsEndpointTests</c> reads off the public list too.
/// </remarks>
[Collection("Api")]
public sealed class AdminJuniorJobAdsEndpointTests(ApiFactory factory)
{
    private const string QueuePath = "/api/v1/admin/junior-job-ads";
    private const string DataIt = "DJh5_yyF_hEM";
    private const string Stockholm = "AvNB_uwa_6n6";
    private const string StockholmsLan = "CifL_Rzy_Mku";

    private readonly MutableFakeDateTimeProvider _clock = new() { UtcNow = DateTimeOffset.UtcNow };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string DecisionPath(Guid id) => $"{QueuePath}/{id}/decision";

    private static async Task<(HttpStatusCode Read, HttpStatusCode Decide)> BothRoutesAsync(HttpClient client, Guid id)
    {
        using var read = await client.GetAsync(QueuePath, Ct);
        using var decide = await client.PostAsJsonAsync(DecisionPath(id), new { decision = "NotJunior" }, Ct);
        return (read.StatusCode, decide.StatusCode);
    }

    [Fact]
    public async Task Both_routes_answer_401_without_a_session_and_change_nothing()
    {
        var id = await AssessedJuniorAsync();

        (await BothRoutesAsync(factory.CreateClient(), id))
            .ShouldBe((HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized));

        (await StoredAsync(id)).Decision.ShouldBeNull();
        (await AuditRowsAsync(id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Both_routes_answer_403_to_an_account_without_the_admin_role_and_change_nothing()
    {
        var id = await AssessedJuniorAsync();
        var user = await AdminAccountsKit.UserAsync(factory, AdminAccountsKit.NewToken(), Ct);

        (await BothRoutesAsync(user, id)).ShouldBe((HttpStatusCode.Forbidden, HttpStatusCode.Forbidden));

        (await StoredAsync(id)).Decision.ShouldBeNull();
        (await AuditRowsAsync(id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_queue_answers_an_administrator_privately_with_its_page_and_its_counts()
    {
        var id = await AssessedJuniorAsync();
        var (admin, _, _) = await AdminAccountsKit.AdminAsync(factory, AdminAccountsKit.NewToken(), Ct);
        // Decided, so the ad sits in the small Decided tab rather than somewhere in the shared database's Shown tab.
        using (var decide = await admin.PostAsJsonAsync(DecisionPath(id), new { decision = "NotJunior" }, Ct))
            decide.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        foreach (var path in new[] { QueuePath, $"{QueuePath}?view=Decided&page=1" })
        {
            using var response = await admin.GetAsync(path, Ct);

            response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
            ShouldBePrivateAndUnstored(response, path);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
            body.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["items", "counts"], ignoreOrder: true, path);
            var page = body.RootElement.GetProperty("items");
            page.EnumerateObject().Select(p => p.Name)
                .ShouldBe(["items", "totalCount", "page", "pageSize", "totalPages"], ignoreOrder: true, path);
            page.GetProperty("page").GetInt32().ShouldBe(1);
            page.GetProperty("pageSize").GetInt32().ShouldBe(ListJuniorJobAdsQuery.PageSize);
            body.RootElement.GetProperty("counts").EnumerateObject().Select(p => p.Name)
                .ShouldBe(["unclear", "shown", "decided"], ignoreOrder: true, path);
            foreach (var item in page.GetProperty("items").EnumerateArray())
            {
                item.EnumerateObject().Select(p => p.Name).ShouldBe(
                    ["id", "title", "companyName", "municipalityName", "publishedAt", "expiresAt", "verdict", "ruleId", "decision"],
                    ignoreOrder: true,
                    "what the ad shows and what the rules and an admin said: never its text");
            }
        }

        using var decided = await admin.GetAsync($"{QueuePath}?view=Decided", Ct);
        using var decidedBody = JsonDocument.Parse(await decided.Content.ReadAsStringAsync(Ct));
        decidedBody.RootElement.GetProperty("counts").GetProperty("decided").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
        var row = decidedBody.RootElement.GetProperty("items").GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("id").GetString() == id.ToString("D"));
        row.GetProperty("title").GetString().ShouldBe("Junior utvecklare");
        row.GetProperty("companyName").GetString().ShouldBe("Exempelbolaget AB");
        row.GetProperty("municipalityName").GetString().ShouldBe("Stockholm");
        row.GetProperty("verdict").GetString().ShouldBe("Junior");
        row.GetProperty("ruleId").GetString().ShouldNotBeNullOrWhiteSpace();
        row.GetProperty("decision").GetString().ShouldBe("NotJunior");
    }

    [Fact]
    public async Task A_decision_answers_204_privately_and_is_audited_on_the_ad_alone()
    {
        var id = await AssessedJuniorAsync();
        var (admin, adminId, _) = await AdminAccountsKit.AdminAsync(factory, AdminAccountsKit.NewToken(), Ct);

        using (var decide = await admin.PostAsJsonAsync(DecisionPath(id), new { decision = "NotJunior" }, Ct))
        {
            decide.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            ShouldBePrivateAndUnstored(decide, "decide");
        }

        (await StoredAsync(id)).Decision.ShouldBe(JuniorDecision.NotJunior);
        var row = (await AuditRowsAsync(id)).ShouldHaveSingleItem();
        row.EventType.ShouldBe("Admin.JuniorDecisionRecorded");
        row.AggregateType.ShouldBe("JobAd");
        row.UserId.ShouldBe(adminId);
        row.Payload.ShouldBeNull("audited by type and ad id alone");

        // Handing the ad back to the rules is a decision too, and audited as one.
        using (var handBack = await admin.PostAsJsonAsync(DecisionPath(id), new { decision = (string?)null }, Ct))
            handBack.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var stored = await StoredAsync(id);
        stored.Decision.ShouldBeNull();
        stored.DecidedAt.ShouldBeNull();
        (await AuditRowsAsync(id)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_decision_about_an_ad_the_queue_does_not_hold_is_a_404_and_is_not_audited()
    {
        var unassessed = await ImportAsync();
        var noSuchAd = Guid.NewGuid();
        var (admin, _, _) = await AdminAccountsKit.AdminAsync(factory, AdminAccountsKit.NewToken(), Ct);

        foreach (var id in new[] { unassessed, noSuchAd })
        {
            using var response = await admin.PostAsJsonAsync(DecisionPath(id), new { decision = "Junior" }, Ct);

            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            ShouldBePrivateAndUnstored(response, id.ToString());
            (await AuditRowsAsync(id)).ShouldBeEmpty();
        }
    }

    // The binder's road (a name no tab has, a page that is no number), then the validator's (a number no tab has, a
    // page out of bounds).
    [Theory]
    [InlineData("?view=Bogus")]
    [InlineData("?view=7")]
    [InlineData("?page=abc")]
    [InlineData("?page=0")]
    [InlineData("?page=1001")]
    public async Task A_queue_filter_the_route_refuses_is_a_400(string query)
    {
        var (admin, _, _) = await AdminAccountsKit.AdminAsync(factory, AdminAccountsKit.NewToken(), Ct);

        using var response = await admin.GetAsync($"{QueuePath}{query}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // A verdict's name is no decision; an integer the converter admits reaches the validator.
    [Theory]
    [InlineData("""{"decision":"Maybe"}""")]
    [InlineData("""{"decision":"Unclear"}""")]
    [InlineData("""{"decision":3}""")]
    public async Task A_decision_the_route_refuses_is_a_400_and_changes_nothing(string json)
    {
        var id = await AssessedJuniorAsync();
        var (admin, _, _) = await AdminAccountsKit.AdminAsync(factory, AdminAccountsKit.NewToken(), Ct);

        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await admin.PostAsync(DecisionPath(id), content, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await StoredAsync(id)).Decision.ShouldBeNull();
        (await AuditRowsAsync(id)).ShouldBeEmpty();
    }

    private static void ShouldBePrivateAndUnstored(HttpResponseMessage response, string route)
    {
        CacheControlHeaderValue cacheControl = response.Headers.CacheControl.ShouldNotBeNull(route);
        cacheControl.Private.ShouldBeTrue(route);
        cacheControl.NoStore.ShouldBeTrue(route);
    }

    /// <summary>The stream's import of a listed Data/IT ad: <c>JobAd.Import</c>, then the save.</summary>
    private async Task<Guid> ImportAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var externalId = TestIds.ExternalId();
        var jobAd = JobAd.Import(
            title: "Junior utvecklare",
            company: Company.Create("Exempelbolaget AB").Value,
            description: "Vi söker en junior utvecklare till vårt team.",
            url: $"https://arbetsformedlingen.se/platsbanken/annonser/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: $"{{\"id\":\"{externalId}\"}}",
            facets: TestFacets.From(occupationGroup: DataIt, municipality: Stockholm, region: StockholmsLan),
            declaredContacts: [],
            publishedAt: _clock.UtcNow.AddMinutes(-1),
            expiresAt: _clock.UtcNow.AddDays(14),
            clock: _clock,
            applicationUrl: null, extractTerms: TestKeywordExtraction.None).Value;
        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(Ct);
        return jobAd.Id.Value;
    }

    /// <summary>An imported ad, classified by the host's own sweep as the Worker's tick would.</summary>
    private async Task<Guid> AssessedJuniorAsync()
    {
        var id = await ImportAsync();
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ClassifyJuniorJobAdsJob>().RunAsync(Ct);

        var assessment = await StoredAsync(id);
        assessment.Verdict.ShouldBe(JuniorVerdict.Junior);
        assessment.Decision.ShouldBeNull();
        return id;
    }

    private async Task<JobAdJuniorAssessment> StoredAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.JuniorAssessments.AsNoTracking().SingleAsync(a => a.Id == new JobAdId(id), Ct);
    }

    private async Task<List<AuditLogEntry>> AuditRowsAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AuditLogEntries.AsNoTracking().Where(a => a.AggregateId == id).ToListAsync(Ct);
    }
}
