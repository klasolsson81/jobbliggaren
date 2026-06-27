using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Admin.BackgroundJobs;
using Jobbliggaren.Application.BackgroundJobs;
using Jobbliggaren.Application.Common.Authorization;
using Jobbliggaren.Domain.Auditing;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Admin;

/// <summary>
/// #204 / TD-83 PR2 — end-to-end integration for the admin trigger/retry mutations over HTTP through
/// the full Mediator pipeline (validation → authorization → AuditBehavior → UnitOfWork). The real
/// <c>HangfireBackgroundJobController</c> is replaced in <see cref="ApiFactory"/> by
/// <see cref="RecordingBackgroundJobController"/> (exposed as <c>factory.Jobs</c>), so no hangfire
/// schema is needed.
///
/// <para>Covers (security must-clear):</para>
/// <list type="bullet">
/// <item><b>Audit-row proof:</b> a successful trigger/retry writes exactly one audit_log row with the
/// expected EventType for the admin actor; a REJECTED retry (NotFound/Conflict) writes NO row
/// (AuditBehavior skips on failure). Mirrors <see cref="MyProfile.UpdateMyProfileAuditTests"/>.</item>
/// <item><b>Trigger allowlist over HTTP:</b> a non-allowlisted id → 400.</item>
/// <item><b>Retry outcome mapping over HTTP:</b> Requeued → 200, JobNotFound → 404,
/// NotInFailedState → 409.</item>
/// </list>
/// </summary>
[Collection("Api")]
public class AdminBackgroundJobsMutationTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    // ---- helpers (mirror AdminAuditLogTests / UpdateMyProfileAuditTests) --------------------------

    private async Task<(HttpClient client, Guid userId)> RegisterAdminClientAsync(CancellationToken ct)
    {
        var client = _factory.CreateClient();
        var email = $"admin-jobs-mut-{Guid.NewGuid():N}@jobbliggaren.test";
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(client, email, ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

        var me = await client.GetAsync("/api/v1/me", ct);
        me.EnsureSuccessStatusCode();
        var meJson = await me.Content.ReadFromJsonAsync<JsonElement>(ct);
        var userId = Guid.Parse(meJson.GetProperty("userId").GetString()!);

        await PromoteToAdminAsync(userId, ct);
        return (client, userId);
    }

    private async Task PromoteToAdminAsync(Guid userId, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        if (!await roleManager.RoleExistsAsync(Roles.Admin))
            (await roleManager.CreateAsync(new IdentityRole<Guid>(Roles.Admin))).Succeeded.ShouldBeTrue();

        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException("User not found.");
        (await userManager.AddToRoleAsync(user, Roles.Admin)).Succeeded.ShouldBeTrue();
    }

    private async Task<int> CountAuditRowsAsync(Guid userId, string eventType, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AuditLogEntries
            .Where(a => a.UserId == userId && a.EventType == eventType)
            .CountAsync(ct);
    }

    private async Task<List<AuditLogEntry>> ReadAuditRowsAsync(Guid userId, string eventType, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AuditLogEntries
            .Where(a => a.UserId == userId && a.EventType == eventType)
            .ToListAsync(ct);
    }

    // ---- audit-row proof: trigger ----------------------------------------------------------------

    [Fact]
    public async Task Trigger_AsAdmin_Returns200_AndWritesOneAuditRow()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterAdminClientAsync(ct);
        var before = await CountAuditRowsAsync(userId, "Admin.RecurringJobTriggered", ct);

        var response = await client.PostAsync(
            $"/api/v1/admin/jobs/recurring/{RecurringJobIds.BackgroundMatching}/trigger", content: null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        body.GetProperty("jobId").GetString().ShouldBe(RecurringJobIds.BackgroundMatching);

        // Side-effect reached the port (fake recorded the trigger).
        _factory.Jobs.Triggered.ShouldContain(RecurringJobIds.BackgroundMatching);

        // Exactly one new audit row for this actor + event type (Art. 30 accountability).
        var after = await CountAuditRowsAsync(userId, "Admin.RecurringJobTriggered", ct);
        after.ShouldBe(before + 1, "a successful trigger writes exactly one audit row");

        var rows = await ReadAuditRowsAsync(userId, "Admin.RecurringJobTriggered", ct);
        rows.ShouldAllBe(r => r.AggregateType == "System.BackgroundJob");
        rows.ShouldAllBe(r => r.AggregateId != Guid.Empty); // RequestId, never Guid.Empty
    }

    // ---- audit-row proof: retry success ----------------------------------------------------------

    [Fact]
    public async Task Retry_RequeuedAsAdmin_Returns200_AndWritesOneAuditRow()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterAdminClientAsync(ct);
        _factory.Jobs.NextRequeueOutcome = RequeueOutcome.Requeued;
        var before = await CountAuditRowsAsync(userId, "Admin.FailedJobRequeued", ct);

        var response = await client.PostAsync(
            "/api/v1/admin/jobs/failed/server%3A1%3Ajob%3A42/retry", content: null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        body.GetProperty("requeued").GetBoolean().ShouldBeTrue();

        var after = await CountAuditRowsAsync(userId, "Admin.FailedJobRequeued", ct);
        after.ShouldBe(before + 1, "a successful requeue writes exactly one audit row");
    }

    // ---- audit-row proof: rejected retry writes NO row -------------------------------------------

    [Fact]
    public async Task Retry_JobNotFound_Returns404_AndWritesNoAuditRow()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterAdminClientAsync(ct);
        _factory.Jobs.NextRequeueOutcome = RequeueOutcome.JobNotFound;
        var before = await CountAuditRowsAsync(userId, "Admin.FailedJobRequeued", ct);

        var response = await client.PostAsync(
            "/api/v1/admin/jobs/failed/server%3A1%3Ajob%3A99/retry", content: null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var after = await CountAuditRowsAsync(userId, "Admin.FailedJobRequeued", ct);
        after.ShouldBe(before, "a rejected (NotFound) requeue must write no audit row");
    }

    [Fact]
    public async Task Retry_NotInFailedState_Returns409_AndWritesNoAuditRow()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterAdminClientAsync(ct);
        _factory.Jobs.NextRequeueOutcome = RequeueOutcome.NotInFailedState;
        var before = await CountAuditRowsAsync(userId, "Admin.FailedJobRequeued", ct);

        var response = await client.PostAsync(
            "/api/v1/admin/jobs/failed/server%3A1%3Ajob%3A7/retry", content: null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var after = await CountAuditRowsAsync(userId, "Admin.FailedJobRequeued", ct);
        after.ShouldBe(before, "a rejected (Conflict) requeue must write no audit row");
    }

    // ---- trigger allowlist over HTTP -------------------------------------------------------------

    [Fact]
    public async Task Trigger_NonAllowlistedId_Returns400_AndWritesNoAuditRow()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, userId) = await RegisterAdminClientAsync(ct);
        var before = await CountAuditRowsAsync(userId, "Admin.RecurringJobTriggered", ct);

        var response = await client.PostAsync(
            "/api/v1/admin/jobs/recurring/not-a-registered-job/trigger", content: null, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // The validator rejects before the handler — the port is never called and no audit row exists.
        _factory.Jobs.Triggered.ShouldNotContain("not-a-registered-job");
        var after = await CountAuditRowsAsync(userId, "Admin.RecurringJobTriggered", ct);
        after.ShouldBe(before, "a validation-rejected trigger must write no audit row");
    }
}
