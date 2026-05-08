using JobbPilot.Application.Auth.Jobs.HardDeleteAccounts;
using JobbPilot.Application.Common.Auditing;
using JobbPilot.Domain.JobSeekers;
using JobbPilot.Infrastructure.Identity;
using JobbPilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace JobbPilot.Infrastructure.Auth;

/// <summary>
/// PostgreSQL + AspNet Identity-implementation av <see cref="IAccountHardDeleter"/>.
/// Korsar AppDbContext (domain-aggregat) och AppIdentityDbContext (via UserManager).
/// Architecture test verifierar att porten endast anropas av HardDeleteAccountsJob.
///
/// Atomicitet-modell (per ADR 0024 D6 + delbeslut 3-tillägg):
/// - Domain-delete + audit-anonymisering atomic via explicit BeginTransactionAsync
///   på AppDbContext (Steg 2 a-g)
/// - Identity-DELETE separat boundary efter transactionen committats (Steg 2 h)
/// - Vid Identity-fail: orphan plockas upp av Steg 0 nästa körning
///
/// Detta är medveten design som följer Clean Arch:s context-isolering — inga
/// distribuerade transaktioner mot samma fysiska Postgres bara för nominell
/// atomicitet.
/// </summary>
public sealed class AccountHardDeleter(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    IAuditTrailEraser auditTrailEraser)
    : IAccountHardDeleter
{
    public async Task<int> CleanupIdentityOrphansAsync(CancellationToken cancellationToken)
    {
        // Cross-context query — Identity och domain har separata DbContexts (ADR 0013)
        // men träffar samma fysiska Postgres. Vi materialiserar båda user-id-listor
        // och diffar i C# (HashSet för O(1) lookup). Volym i Fas 1 < 1000 users
        // → C#-side diff är OK; SQL JOIN över schemas hade krävt raw SQL och
        // kringgått EF-modellen.
        var identityUserIds = await userManager.Users
            .AsNoTracking()
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

        var domainUserIds = (await db.JobSeekers
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Select(js => js.UserId)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var orphanIds = identityUserIds.Where(id => !domainUserIds.Contains(id)).ToList();

        var cleaned = 0;
        foreach (var orphanId in orphanIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var user = await userManager.FindByIdAsync(orphanId.ToString());
            if (user is null) continue; // Race: Identity redan rensad mellan SELECT och DELETE

            var result = await userManager.DeleteAsync(user);
            if (result.Succeeded) cleaned++;
        }

        return cleaned;
    }

    public async Task<IReadOnlyList<Guid>> GetAccountsReadyForHardDeleteAsync(
        DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters för att se soft-deletade JobSeekers. Bara de vars
        // deleted_at < cutoff är mogna för hard-delete (30-dagars-fönstret
        // utgånget).
        return await db.JobSeekers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(js => js.DeletedAt != null && js.DeletedAt < cutoff)
            .Select(js => js.Id.Value)
            .ToListAsync(cancellationToken);
    }

    public async Task HardDeleteAccountAsync(Guid jobSeekerId, CancellationToken cancellationToken)
    {
        var jsId = new JobSeekerId(jobSeekerId);

        // Hämta JobSeeker (IgnoreQueryFilters — den ÄR soft-deletad per
        // GetAccountsReadyForHardDeleteAsync-kontraktet).
        var jobSeeker = await db.JobSeekers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(js => js.Id == jsId, cancellationToken);

        if (jobSeeker is null)
            return; // Idempotent — redan borta (race vid concurrent runs)

        var userId = jobSeeker.UserId;

        // Hämta alla user-ägda aggregat. IgnoreQueryFilters för att se
        // soft-deletade barn också (de är raderade vid DeleteAccountCommand).
        // FK CASCADE i DB tar Application→{FollowUps, Notes} + Resume→Versions
        // när vi RemoveRange:ar parents.
        var applications = await db.Applications
            .IgnoreQueryFilters()
            .Where(a => a.JobSeekerId == jsId)
            .ToListAsync(cancellationToken);

        var resumes = await db.Resumes
            .IgnoreQueryFilters()
            .Where(r => r.JobSeekerId == jsId)
            .ToListAsync(cancellationToken);

        // Steg 2 a — Öppna explicit transaction (UoWBehavior är inte i pipelinen
        // för worker-jobb-anrop direkt mot porten).
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            // Steg 2 b — Anonymisera audit-trail. Deltar i samma transaction
            // (ExecuteSqlAsync respekterar ambient transaction).
            await auditTrailEraser.AnonymizeUserAuditTrailAsync(userId, cancellationToken);

            // Steg 2 c-e — Hard-delete domain-aggregat (FK CASCADE tar barnen).
            db.Applications.RemoveRange(applications);
            db.Resumes.RemoveRange(resumes);
            db.JobSeekers.Remove(jobSeeker);

            // Steg 2 f — SaveChanges + Steg 2 g — Commit.
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        // Steg 2 h — Identity-DELETE separat boundary. Om denna failer plockas
        // raden upp av Steg 0 (CleanupIdentityOrphansAsync) i nästa körning.
        // Idempotent — UserManager.DeleteAsync på redan borttagen användare
        // returnerar IdentityResult.Failed, vilket vi medvetet ignorerar
        // (orphan-loopen kan retry:a separat).
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is not null)
            await userManager.DeleteAsync(user);
    }
}
