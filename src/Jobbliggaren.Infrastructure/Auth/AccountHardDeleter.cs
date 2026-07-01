using Jobbliggaren.Application.Auth.Jobs.HardDeleteAccounts;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Infrastructure.Auth;

/// <summary>
/// PostgreSQL + AspNet Identity-implementation av <see cref="IAccountHardDeleter"/>.
/// Korsar AppDbContext (domain-aggregat) och AppIdentityDbContext (via UserManager).
/// Architecture test verifierar att porten endast anropas av HardDeleteAccountsJob.
///
/// Atomicitet-modell (per ADR 0024 D6 + delbeslut 3-tillägg + TD-13 C6):
/// - Domain-delete + audit-anonymisering + crypto-erasure (per-användare-DEK,
///   ADR 0049 Beslut 2) atomic via explicit BeginTransactionAsync på
///   AppDbContext (Steg 2 a-g)
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
    IAuditTrailEraser auditTrailEraser,
    IUserDataKeyStore dataKeyStore)
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

        // GDPR Art. 17-cascade för aggregat utan databas-FK (ADR 0011
        // strongly-typed soft-reference-mönster). SavedSearches/RecentJobSearches
        // saknar HasOne-FK till JobSeekers → måste raderas explicit för att inte
        // lämna orphaned rader vid hard-delete (security-auditor F6 P4a 2026-05-20).
        // Pre-existing SavedSearches-lucka fixas in-block (CLAUDE.md §9.6 —
        // samma fas, samma blast-radius som RecentJobSearches-introduktionen).
        var savedSearches = await db.SavedSearches
            .IgnoreQueryFilters()
            .Where(s => s.JobSeekerId == jsId)
            .ToListAsync(cancellationToken);

        var recentSearches = await db.RecentJobSearches
            .Where(r => r.JobSeekerId == jsId)
            .ToListAsync(cancellationToken);

        // F6 P5 Punkt 2 Del A — SavedJobAd cascade-paritet (ADR 0024 amend
        // 2026-05-23): saved_job_ads saknar DB-FK till job_seekers per
        // ADR 0011 strongly-typed soft-reference-mönster, samma blast-radius
        // som SavedSearches/RecentJobSearches.
        var savedJobAds = await db.SavedJobAds
            .Where(s => s.JobSeekerId == jsId)
            .ToListAsync(cancellationToken);

        // ADR 0080 Vag 4 (Beslut 1) — UserJobAdMatch är ett FK-löst by-identity-aggregat
        // (keyed by UserId, ADR 0058/0059 soft-reference) → måste raderas explicit i Art.
        // 17-cascaden, annars orphan:as background-match-rader vid hard-delete. Wirat redan i
        // PR-1 (defense-in-depth) trots att skrivvägen (Worker-scan) landar i PR-3 —
        // utfästelsen i ADR 0080 ska aldrig kunna tappas. IgnoreQueryFilters tar även
        // soft-deletade match-rader.
        var userJobAdMatches = await db.UserJobAdMatches
            .IgnoreQueryFilters()
            .Where(m => m.UserId == userId)
            .ToListAsync(cancellationToken);

        // ADR 0087 D3 (#311 PR-3) — CompanyWatch is an FK-less by-UserId aggregate (ADR 0058/0059
        // soft-reference; the watched org.nr is user-owned PII per D8(b) — it reveals WHOM the user
        // follows). Like UserJobAdMatch it must be deleted EXPLICITLY in the Art. 17 cascade or its
        // rows orphan on hard-delete. IgnoreQueryFilters also takes soft-deleted (unfollowed) rows.
        var companyWatches = await db.CompanyWatches
            .IgnoreQueryFilters()
            .Where(w => w.UserId == userId)
            .ToListAsync(cancellationToken);

        // ADR 0087 D5 (#311 PR-4) — FollowedCompanyAdHit is an FK-less by-UserId aggregate (ADR
        // 0058/0059 soft-reference; it records WHICH followed-employer ad a user was notified about —
        // user-owned personal data). Like UserJobAdMatch/CompanyWatch it must be deleted EXPLICITLY in
        // the Art. 17 cascade or its rows orphan on hard-delete. IgnoreQueryFilters also takes
        // soft-deleted rows.
        var followedCompanyAdHits = await db.FollowedCompanyAdHits
            .IgnoreQueryFilters()
            .Where(h => h.UserId == userId)
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
            db.SavedSearches.RemoveRange(savedSearches);
            db.RecentJobSearches.RemoveRange(recentSearches);
            db.SavedJobAds.RemoveRange(savedJobAds);
            db.UserJobAdMatches.RemoveRange(userJobAdMatches);
            db.CompanyWatches.RemoveRange(companyWatches);
            db.FollowedCompanyAdHits.RemoveRange(followedCompanyAdHits);
            db.JobSeekers.Remove(jobSeeker);

            // GDPR Art. 17 (#370, found by the #268 audit) — ParsedResume is an FK-less
            // by-JobSeekerId aggregate (ADR 0011 strongly-typed soft-reference; the raw-CV
            // staging aggregate, ADR 0074). Like UserJobAdMatch/SavedSearches it must be deleted
            // EXPLICITLY or its rows orphan on hard-delete. Crypto-erasure (DeleteDataKeysAsync
            // below) only makes the DEK-encrypted columns (raw_text/parsed_content_enc) unreadable
            // — it does NOT remove the PLAINTEXT columns (source_file_name, frequently the data
            // subject's name; job_seeker_id), so the rows themselves must go. ExecuteDeleteAsync =
            // a SQL DELETE with NO DEK/jsonb materialization (parity ParsedResumeRetentionJob),
            // and it participates in the ambient BeginTransactionAsync transaction (rollback-safe,
            // same guarantee as DeleteDataKeysAsync). IgnoreQueryFilters so any soft-/status-state
            // row is included. Idempotent (0 rows = no-op).
            await db.ParsedResumes
                .IgnoreQueryFilters()
                .Where(p => p.JobSeekerId == jsId)
                .ExecuteDeleteAsync(cancellationToken);

            // Steg 2 e2 — Crypto-erasure (TD-13 ADR 0049 Beslut 2 + C6,
            // GDPR Art. 17). Kastar användarens per-användare-DEK INOM samma
            // transaktion → backup-resident ciphertext (cover_letter/
            // application_notes.content/follow_ups.note/resume_versions.
            // content_enc/parsed_resumes.raw_text/parsed_content_enc) blir
            // omedelbart olesbar. ExecuteDeleteAsync deltar
            // i den ambienta BeginTransactionAsync-transaktionen (dotnet-
            // architect-verifierad 2026-05-19, Microsoft Learn): vid rollback
            // rullas DEK-deletet med aggregat-deletet → ingen partiell
            // Art. 17-erasure. Idempotent (0 DEK-rader = no-op).
            await dataKeyStore.DeleteDataKeysAsync(jsId, cancellationToken);

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
