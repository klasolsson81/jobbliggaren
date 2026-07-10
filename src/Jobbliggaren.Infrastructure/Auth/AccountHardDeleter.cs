using Jobbliggaren.Application.Auth.Jobs.HardDeleteAccounts;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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
public sealed partial class AccountHardDeleter(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    IAuditTrailEraser auditTrailEraser,
    IUserDataKeyStore dataKeyStore,
    IDateTimeProvider clock,
    ILogger<AccountHardDeleter> logger)
    : IAccountHardDeleter
{
    /// <summary>
    /// #508 (ADR 0024 D6) orphan-sweep grace window. Registration commits the Identity
    /// user first (own SaveChanges) and the JobSeeker later (UnitOfWork, with a Redis
    /// roundtrip in between) — ADR 0024's deliberate two-boundary model. An Identity user
    /// with no JobSeeker that is YOUNGER than this window is therefore presumed to be an
    /// in-flight registration, not an orphan, and is never swept — sweeping it would
    /// permanently delete a live account being created (the TOCTOU this control hardens;
    /// the CTO bind rejects the query-presence-only mechanic C). 1 h is far wider than the
    /// real registration window (sub-second) so a slow/stalled registration is never
    /// mistaken for an orphan; the cost of waiting one extra daily cron cycle to reap a
    /// genuine orphan is nil. Hardcoded in Fas 1 — the same hardcoded-constant pattern as
    /// <see cref="HardDeleteAccountsJob"/>.RestoreWindowDays (the pattern, not the value:
    /// that window is 30 days, this one is 1 h); flips to IOptions if the policy ever changes.
    /// </summary>
    private static readonly TimeSpan OrphanGraceWindow = TimeSpan.FromHours(1);

    public async Task<int> CleanupIdentityOrphansAsync(CancellationToken cancellationToken)
    {
        // Cross-context query — Identity och domain har separata DbContexts (ADR 0013)
        // men träffar samma fysiska Postgres. Vi materialiserar båda sidor (id + created_at
        // för Identity) och diffar i C# (HashSet för O(1) lookup). Volym i Fas 1 < 1000 users
        // → C#-side diff är OK; SQL JOIN över schemas hade krävt raw SQL och kringgått
        // EF-modellen.
        var identityUsers = await userManager.Users
            .AsNoTracking()
            .Select(u => new { u.Id, u.CreatedAt })
            .ToListAsync(cancellationToken);

        var domainUserIds = (await db.JobSeekers
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Select(js => js.UserId)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        // #508 forward-orphan grace filter (ADR 0024 D6). Sweep a JobSeeker-less Identity
        // user ONLY if it is OLDER than the grace window; a younger one is presumed
        // mid-registration (Identity committed, JobSeeker not yet) and left alone. Root
        // cause (non-atomic two-boundary registration) is deliberately NOT "fixed" here —
        // we harden the compensating control, we do not introduce a cross-context tx.
        // Skew assumption: CreatedAt is stamped by Postgres now() while the threshold uses
        // the app clock (IDateTimeProvider). The 1 h window dwarfs any realistic skew (Fas 1
        // runs DB + Worker on one host, ADR 0066; prod relies on NTP), so skew cannot make
        // the window ineffective; the only skew failure mode (app clock >1 h AHEAD of the DB)
        // is a gross misconfiguration, not drift.
        var graceThreshold = clock.UtcNow - OrphanGraceWindow;
        var orphanIds = identityUsers
            .Where(u => !domainUserIds.Contains(u.Id) && u.CreatedAt <= graceThreshold)
            .Select(u => u.Id)
            .ToList();

        // #508 reverse-orphan detector (defense-in-depth, log-only). A JobSeeker whose
        // UserId has no Identity user is the mirror of the same TOCTOU race — the account is
        // locked out and can never exercise Art. 17. We SURFACE it (Warning) for remediation
        // but never delete it here (a separate concern, #524). Count only — no name/email/CV
        // PII is logged (CLAUDE.md §5); the runbook §3.3 reverse-orphan query surfaces the
        // UserId set to ops on demand.
        var identityUserIds = identityUsers.Select(u => u.Id).ToHashSet();
        var reverseOrphanCount = domainUserIds.Count(id => !identityUserIds.Contains(id));
        if (reverseOrphanCount > 0)
            LogReverseOrphansDetected(logger, reverseOrphanCount);

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

            // GDPR Art. 17 (Fas 4b PR-9a, ADR 0093 §D5, DPIA M-F1) — ResumeFile is an FK-less
            // by-JobSeekerId aggregate (the original-file binary store). Its content column is the
            // Form C ciphertext (crypto-erased below), BUT its plaintext metadata (file_name —
            // redacted yet still metadata, byte_size, content_type) survives crypto-erasure, so the
            // ROWS must be deleted (exact ParsedResume precedent above). ExecuteDeleteAsync = a SQL
            // DELETE with NO bytea/DEK materialization, in the ambient transaction. Owner-scoped so
            // ALL of the user's originals go (even any whose parsed sibling was already swept).
            // Idempotent (0 rows = no-op).
            await db.ResumeFiles
                .Where(f => f.JobSeekerId == jsId)
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

    // #508 — reverse-orphan är utelåsta konton (JobSeeker utan Identity-user) som aldrig
    // kan utöva Art. 17. Warning-nivå (alertbar signal), count-only (ingen PII i loggen,
    // CLAUDE.md §5). EventId i HardDeleteAccounts-serien (25xx). Driftmeddelande på svenska
    // per områdets konvention (jfr HardDeleteAccountsJob + runbook account-deletion.md §3.2).
    [LoggerMessage(EventId = 2503, Level = LogLevel.Warning,
        Message = "CleanupIdentityOrphansAsync: {Count} reverse-orphan JobSeeker(s) saknar Identity-user "
            + "(utelåst konto, kan ej utöva Art. 17) — loggas för utredning, raderas ej här (#508/#524)")]
    private static partial void LogReverseOrphansDetected(ILogger logger, int count);
}
