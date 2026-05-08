using JobbPilot.Application.Auth.Jobs.HardDeleteAccounts;
using JobbPilot.Domain.Auditing;
using JobbPilot.Domain.Common;
using JobbPilot.Domain.JobSeekers;
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
