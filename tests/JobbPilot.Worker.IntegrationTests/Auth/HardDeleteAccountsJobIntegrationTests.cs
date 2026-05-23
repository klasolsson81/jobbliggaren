using JobbPilot.Application.Auth.Jobs.HardDeleteAccounts;
using JobbPilot.Domain.Auditing;
using JobbPilot.Domain.Common;
using JobbPilot.Domain.JobAds;
using JobbPilot.Domain.JobSeekers;
using JobbPilot.Domain.RecentJobSearches;
using JobbPilot.Domain.SavedJobAds;
using JobbPilot.Domain.SavedSearches;
using JobbPilot.Infrastructure.Identity;
using JobbPilot.Infrastructure.Persistence;
using JobbPilot.Worker.IntegrationTests.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace JobbPilot.Worker.IntegrationTests.Auth;

/// <summary>
/// End-to-end smoke-test för <see cref="HardDeleteAccountsJob"/> mot riktig
/// Postgres + AspNet Identity (Testcontainers). Verifierar 3-stegs-algoritmen
/// per ADR 0024 D6: orphan-cleanup → hämta mogna → cascade hard-delete +
/// audit-anonymisering + Identity-DELETE.
///
/// Märkt <c>[Trait("Category", "SmokeTest")]</c>.
/// </summary>
[Collection("Worker")]
[Trait("Category", "SmokeTest")]
public class HardDeleteAccountsJobIntegrationTests(WorkerTestFixture fixture)
{
    private readonly WorkerTestFixture _fixture = fixture;

    private const int RestoreWindowDays = 30;

    [Fact]
    public async Task RunAsync_HardDeletesEligibleAccount_AnonymizesAudit_RemovesIdentity()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var oldDeletedAt = now.AddDays(-(RestoreWindowDays + 1)); // utanför fönstret

        // Setup: Identity-user + JobSeeker (soft-deletad > 30d) + audit-rad
        var (userId, jobSeekerId) = await SeedSoftDeletedAccountAsync(oldDeletedAt, ct);
        var auditAggregateId = Guid.NewGuid();
        await SeedAuditEntryAsync(userId, auditAggregateId, ct);

        // Akt
        await RunJobAsync(now, ct);

        // Verifiera: hard-delete + Identity borta + audit anonymiserad
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var jobSeeker = await db.JobSeekers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(js => js.Id == jobSeekerId, ct);
        jobSeeker.ShouldBeNull("JobSeeker ska vara hard-deletad");

        var identityUser = await userManager.FindByIdAsync(userId.ToString());
        identityUser.ShouldBeNull("Identity-rad ska vara borta efter Steg 2 h");

        var auditEntry = await db.AuditLogEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.AggregateId == auditAggregateId, ct);
        auditEntry.ShouldNotBeNull("audit-raden bevaras 90 dagar för accountability");
        auditEntry.UserId.ShouldBeNull("user_id ska anonymiseras");
        auditEntry.IpAddress.ShouldBeNull();
        auditEntry.UserAgent.ShouldBeNull();
    }

    [Fact]
    public async Task RunAsync_DoesNotHardDeleteAccountsWithin30Days()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var recentDeletedAt = now.AddDays(-10); // inom restore-fönstret

        var (userId, jobSeekerId) = await SeedSoftDeletedAccountAsync(recentDeletedAt, ct);

        await RunJobAsync(now, ct);

        // Verifiera: JobSeeker fortfarande finns (soft-deleted), Identity finns
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var jobSeeker = await db.JobSeekers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(js => js.Id == jobSeekerId, ct);
        jobSeeker.ShouldNotBeNull("nyligen soft-deleted JobSeeker ska BEHÅLLAS inom restore-fönstret");
        jobSeeker.DeletedAt.ShouldNotBeNull();

        var identityUser = await userManager.FindByIdAsync(userId.ToString());
        identityUser.ShouldNotBeNull("Identity-rad ska finnas tills hard-delete-fönstret gått ut");
    }

    [Fact]
    public async Task CleanupIdentityOrphans_RemovesOrphanIdentityRowsWithoutMatchingJobSeeker()
    {
        var ct = TestContext.Current.CancellationToken;

        // Seed: Identity-user UTAN matchande JobSeeker (orphan från tidigare körning)
        var orphanEmail = $"orphan-{Guid.NewGuid():N}@test.local";
        Guid orphanUserId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = orphanEmail, Email = orphanEmail };
            var result = await userManager.CreateAsync(user, "OrphanPass123!");
            result.Succeeded.ShouldBeTrue("seed: Identity-user måste skapas");
            orphanUserId = user.Id;
        }

        // Akt: kör jobbet (Steg 0 ska plocka upp orphan)
        await RunJobAsync(DateTimeOffset.UtcNow, ct);

        // Verifiera: orphan borta
        using (var scope = _fixture.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var orphan = await userManager.FindByIdAsync(orphanUserId.ToString());
            orphan.ShouldBeNull("orphan Identity-rad ska rensas av Steg 0");
        }
    }

    [Fact]
    public async Task RunAsync_CascadesHardDelete_ToSavedSearchesAndRecentJobSearches()
    {
        // GDPR Art. 17-cascade (ADR 0060 Mekanik-not 5 + ADR 0024-amend
        // 2026-05-20): SavedSearches och RecentJobSearches saknar databas-FK
        // till JobSeekers (ADR 0011 strongly-typed soft-reference). De måste
        // raderas explicit i HardDeleteAccountAsync — annars orphan-PII (q-
        // fritext, namn-värdig sökterm) blir kvar efter konto-radering.
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var oldDeletedAt = now.AddDays(-(RestoreWindowDays + 1));

        var (userId, jobSeekerId) = await SeedSoftDeletedAccountAsync(oldDeletedAt, ct);

        // Seed SavedSearch + RecentJobSearch för seekern
        Guid savedSearchId;
        Guid recentSearchId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clock = new FixedClock(oldDeletedAt.AddDays(-2));
            var criteria = SearchCriteria.Create(
                ["12345"], ["stockholm"], "developer", JobAdSortBy.PublishedAtDesc).Value;

            var saved = SavedSearch.Create(jobSeekerId, "Mitt sök", criteria, false, clock).Value;
            db.SavedSearches.Add(saved);

            var recent = RecentJobSearch.Capture(jobSeekerId, criteria, 10, clock.UtcNow);
            db.RecentJobSearches.Add(recent);

            await db.SaveChangesAsync(ct);
            savedSearchId = saved.Id.Value;
            recentSearchId = recent.Id.Value;
        }

        await RunJobAsync(now, ct);

        using var verifyScope = _fixture.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var savedAfter = await verifyDb.SavedSearches
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == new SavedSearchId(savedSearchId), ct);
        savedAfter.ShouldBeNull("SavedSearch ska cascade-raderas vid hard-delete (GDPR Art. 17)");

        var recentAfter = await verifyDb.RecentJobSearches
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == new RecentJobSearchId(recentSearchId), ct);
        recentAfter.ShouldBeNull("RecentJobSearch ska cascade-raderas vid hard-delete (GDPR Art. 17)");
    }

    [Fact]
    public async Task RunAsync_CascadesHardDelete_ToSavedJobAds()
    {
        // F6 P5 Punkt 2 Del A — SavedJobAd cascade-paritet (ADR 0024-amend
        // 2026-05-23): saved_job_ads saknar databas-FK till job_seekers
        // (ADR 0011 strongly-typed soft-reference). Måste raderas explicit i
        // HardDeleteAccountAsync — annars orphan-rader efter konto-radering.
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var oldDeletedAt = now.AddDays(-(RestoreWindowDays + 1));

        var (_, jobSeekerId) = await SeedSoftDeletedAccountAsync(oldDeletedAt, ct);

        Guid savedJobAdId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var savedJobAdJobAdId = new JobAdId(Guid.NewGuid());
            var saved = SavedJobAd.Save(
                jobSeekerId, savedJobAdJobAdId, oldDeletedAt.AddDays(-2));
            db.SavedJobAds.Add(saved);
            await db.SaveChangesAsync(ct);
            savedJobAdId = saved.Id.Value;
        }

        await RunJobAsync(now, ct);

        using var verifyScope = _fixture.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();

        var savedJobAdAfter = await verifyDb.SavedJobAds
            .AsNoTracking()
            .FirstOrDefaultAsync(
                s => s.Id == new SavedJobAdId(savedJobAdId), ct);
        savedJobAdAfter.ShouldBeNull(
            "SavedJobAd ska cascade-raderas vid hard-delete (GDPR Art. 17, ADR 0024 amend 2026-05-23)");
    }

    // ─── Helpers ───

    private async Task<(Guid UserId, JobSeekerId JobSeekerId)> SeedSoftDeletedAccountAsync(
        DateTimeOffset deletedAt, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var email = $"hd-{Guid.NewGuid():N}@test.local";
        var user = new ApplicationUser { UserName = email, Email = email };
        var result = await userManager.CreateAsync(user, "HardDeletePass123!");
        result.Succeeded.ShouldBeTrue("seed: Identity-user måste skapas");

        // JobSeeker.Register tar IDateTimeProvider — vi använder en FixedClock
        // för registreringstid och en separat FixedClock för soft-delete.
        var registerClock = new FixedClock(deletedAt.AddDays(-1)); // registrerades före radering
        var seekerResult = JobSeeker.Register(user.Id, "HardDelete Seed", registerClock);
        seekerResult.IsSuccess.ShouldBeTrue();
        var jobSeeker = seekerResult.Value;

        // Soft-delete med fix klocka för att simulera utgånget restore-fönster
        jobSeeker.SoftDelete(new FixedClock(deletedAt));

        db.JobSeekers.Add(jobSeeker);
        await db.SaveChangesAsync(ct);

        return (user.Id, jobSeeker.Id);
    }

    private async Task SeedAuditEntryAsync(Guid userId, Guid aggregateId, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var entry = AuditLogEntry.Create(
            occurredAt: DateTimeOffset.UtcNow,
            correlationId: Guid.NewGuid(),
            userId: userId,
            eventType: "Account.Deleted",
            aggregateType: "JobSeeker",
            aggregateId: aggregateId,
            ipAddress: "10.0.0.1",
            userAgent: "TestAgent");

        db.AuditLogEntries.Add(entry);
        await db.SaveChangesAsync(ct);
    }

    private async Task RunJobAsync(DateTimeOffset now, CancellationToken ct)
    {
        using var scope = _fixture.Services.CreateScope();
        var hardDeleter = scope.ServiceProvider.GetRequiredService<IAccountHardDeleter>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger<HardDeleteAccountsJob>();
        var job = new HardDeleteAccountsJob(hardDeleter, new FixedClock(now), logger);
        await job.RunAsync(ct);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
