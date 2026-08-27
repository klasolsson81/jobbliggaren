using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Dev.Configuration;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes.Files;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Application.Dev.Commands.ResetMyData;

/// <summary>
/// DEV-ONLY — REMOVE BEFORE LAUNCH (Klas). Clears the current user's onboarding-
/// relevant data so the welcome/setup flow can be re-tested from scratch. Mirrors
/// <c>DeleteAccountCommandHandler</c>'s owner-resolution + guards, but is narrower:
/// it never touches Applications nor the account (<c>JobSeeker</c>) itself.
///
/// <para><b>The criterion is "everything that makes /oversikt look like a started account"</b>
/// (CTO 2026-08-27), not "everything the setup flow writes". The narrower wording was
/// measurably false of this handler before it was written down: <c>SavedJobAd</c> and
/// <c>RecentJobSearch</c> are written from <c>/jobb</c>. Stating the real criterion is what
/// makes the exclusions below principled rather than arbitrary. Each arm below carries its
/// own delete strategy inline, mirroring that aggregate's own DELETE convention.</para>
///
/// <para><b>Deliberately NOT cleared.</b> <c>user_data_keys</c> — the DEK is kept, because
/// Applications survive this reset and their <c>cover_letter</c>, notes and follow-ups are
/// encrypted under it; deleting it would silently brick data the reset is meant to preserve.
/// <c>audit_log</c> — Art. 5(2) accountability. <c>resume_finding_statuses</c> — a child
/// inside the <c>Resume</c> aggregate, DEK-free by shape, and a fresh import gets a new
/// <c>ResumeId</c>, so nothing collides. Saved searches and the company-watch surfaces — a
/// different bounded context with its own change-reason. Applications and their children.</para>
///
/// <para><b>Known limitation, measured rather than assumed.</b> The lifecycle watermarks on
/// <c>JobSeeker</c> are left set. A reset account that has opted IN to background matching
/// does not regain a new registration's cold-start window, because <c>BackgroundMatchingJob</c>
/// reads <c>LastMatchScanAt ?? now.AddDays(-ColdStartDays)</c>. Nulling it would require
/// weakening <c>AdvanceMatchScan</c>'s monotonic-and-clamped invariant — a live affordance to
/// corrupt a watermark that would outlive this throwaway tool — and the next nightly run's
/// SSYK gate would advance it again regardless. A newly registered account is opted OUT by
/// default, so it is unaffected.</para>
///
/// <para>Tracked deletes (<c>RemoveRange</c>) rather than <c>ExecuteDeleteAsync</c>:
/// consistent with the two existing hard-delete handlers, keeps everything in the one
/// UnitOfWork transaction, and stays compatible with the InMemory unit-test provider.</para>
///
/// All mutations are flushed by <c>UnitOfWorkBehavior</c> (atomic). Tolerant of a
/// missing JobSeeker (returns Success) so the dev can call it idempotently.
/// </summary>
public sealed class ResetMyDataCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IOptions<DevToolsOptions> devTools)
    : ICommandHandler<ResetMyDataCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(ResetMyDataCommand command, CancellationToken cancellationToken)
    {
        // Gate TWO of two, and not redundant with the map gate in Program.cs. That one decides
        // whether the ROUTE exists; this decides whether the OPERATION runs, so the primitive
        // stays refused if it is ever reached by another caller — a second endpoint, a job, a
        // test host that maps everything. Same two-independent-structural-gates shape the
        // confirm-email seam already has, and fail-closed for the same reason: the flag defaults
        // to false.
        if (!devTools.Value.EnableResetMyData)
        {
            return Result.Failure<Guid>(
                DomainError.Validation(
                    "Dev.ResetMyDataDisabled",
                    "Återställning av testdata är avstängd."));
        }

        // Defense-in-depth: AuthorizationBehavior normally checks the
        // IAuthenticatedRequest marker, but we don't take a hard dependency on the
        // pipeline being configured (mirrors DeleteAccountCommandHandler).
        if (!currentUser.UserId.HasValue)
            return Result.Failure<Guid>(
                DomainError.Validation(
                    "Dev.NotAuthenticated",
                    "Inloggning krävs för att återställa dina data."));

        var userId = currentUser.UserId.Value;

        // Active JobSeeker only (no IgnoreQueryFilters) — a soft-deleted account has
        // nothing meaningful to reset. Tolerant: if there is no seeker yet, the dev
        // simply has nothing to clear → Success (idempotent).
        var jobSeeker = await db.JobSeekers
            .FirstOrDefaultAsync(js => js.UserId == userId, cancellationToken);

        // Tolerant: nothing to clear is a success, not a failure. It carries the USER id
        // because AuditLogEntry.Create refuses Guid.Empty — see the command's docblock.
        if (jobSeeker is null)
            return Result.Success(userId);

        // CVs — soft-delete via the aggregate's own method (cascades to Versions).
        // Global query filter already excludes any already soft-deleted rows.
        var resumes = await db.Resumes
            .Where(r => r.JobSeekerId == jobSeeker.Id)
            .Include(r => r.Versions)
            .ToListAsync(cancellationToken);
        foreach (var resume in resumes)
            resume.SoftDelete(clock);

        // Point the account away from a CV that is now invisible (parity
        // DeleteResumeCommandHandler). Idempotent in the aggregate; no guard here.
        jobSeeker.UnsetPrimaryResume(clock);

        // Parsed-CV staging artifacts — soft-delete via Discard (sets DeletedAt).
        var parsedResumes = await db.ParsedResumes
            .Where(p => p.JobSeekerId == jobSeeker.Id)
            .ToListAsync(cancellationToken);
        foreach (var parsed in parsedResumes)
            parsed.Discard(clock);

        // The uploaded originals — the union of the two cascades the product already performs
        // (DeleteResumeCommandHandler for a promoted original, DiscardParsedResumeCommandHandler
        // for a rejected one). Without this the raw PDF/DOCX survived every reset: the retention
        // sweep deliberately does not collect a PROMOTED original. Owner-scoped, and projects
        // ONLY the id — never the sealed bytea (§5 minimisation).
        var fileIds = await db.ResumeFiles
            .Where(f => f.JobSeekerId == jobSeeker.Id)
            .Select(f => f.Id)
            .ToListAsync(cancellationToken);
        foreach (var fileId in fileIds)
            db.ResumeFiles.Remove(ResumeFile.DeleteHandle(fileId));

        // "Sökta annonser" — saved bookmarks (hard-delete). Unsave first because the
        // aggregate raises its domain event there; the audit row for this reset is written
        // by AuditBehavior off the command's own marker, not off that event.
        var savedJobAds = await db.SavedJobAds
            .Where(s => s.JobSeekerId == jobSeeker.Id)
            .ToListAsync(cancellationToken);
        foreach (var saved in savedJobAds)
            saved.Unsave(clock.UtcNow);
        db.SavedJobAds.RemoveRange(savedJobAds);

        // "Sökta annonser" — auto-captured recent searches (hard-delete).
        var recentSearches = await db.RecentJobSearches
            .Where(r => r.JobSeekerId == jobSeeker.Id)
            .ToListAsync(cancellationToken);
        db.RecentJobSearches.RemoveRange(recentSearches);

        // Graded matches — the setup's OUTPUT. Empty preferences beside live graded matches is a
        // state no lifecycle in src/ produces, and the surfaces that read them would describe
        // preferences the account no longer has. Keyed on UserId, NOT JobSeekerId (#868).
        var matches = await db.UserJobAdMatches
            .Where(m => m.UserId == userId)
            .ToListAsync(cancellationToken);
        db.UserJobAdMatches.RemoveRange(matches);

        // Reset stated match preferences → Empty so hasStatedDesiredOccupation
        // becomes false and the welcome modal re-triggers (tracked mutation).
        jobSeeker.UpdateMatchPreferences(MatchPreferences.Empty, clock);

        // SaveChanges happens via UnitOfWorkBehavior — atomic across all the above.
        return Result.Success(userId);
    }
}
