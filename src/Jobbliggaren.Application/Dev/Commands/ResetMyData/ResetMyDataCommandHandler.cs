using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Dev.Commands.ResetMyData;

/// <summary>
/// DEV-ONLY — REMOVE BEFORE LAUNCH (Klas). Clears the current user's onboarding-
/// relevant data so the welcome/setup flow can be re-tested from scratch. Mirrors
/// <c>DeleteAccountCommandHandler</c>'s owner-resolution + guards, but is narrower:
/// it never touches Applications nor the account (<c>JobSeeker</c>) itself.
///
/// <para><b>Per-entity delete strategy</b> (mirrors each aggregate's own DELETE
/// convention so rows behave consistently with the global soft-delete query filter):</para>
/// <list type="bullet">
/// <item><b>Resume</b> → <c>SoftDelete(clock)</c> (cascades to its Versions); the
/// aggregate carries <c>DeletedAt</c> → the query filter hides it from the UI.
/// Mirrors DeleteAccount.</item>
/// <item><b>ParsedResume</b> → <c>Discard(clock)</c> (soft-delete via <c>DeletedAt</c>;
/// retained for audit until the staging-retention sweep, per ADR 0074). The staging
/// artifact carries <c>DeletedAt</c> → query filter hides it.</item>
/// <item><b>SavedJobAd</b> → hard-delete (no <c>DeletedAt</c>) via
/// <c>Unsave(now)</c> + <c>RemoveRange</c>, mirroring <c>UnsaveJobAdCommandHandler</c>
/// (raises the unsaved domain event before removal so the audit pipeline observes it).</item>
/// <item><b>RecentJobSearch</b> → hard-delete (no <c>DeletedAt</c>) via
/// <c>RemoveRange</c>, mirroring <c>DeleteRecentSearchCommandHandler</c> (auto-capture
/// cache rows have no audit-trail-worthiness).</item>
/// <item><b>MatchPreferences</b> → <c>UpdateMatchPreferences(MatchPreferences.Empty, clock)</c>
/// (tracked mutation; persisted by the UnitOfWork pipeline).</item>
/// </list>
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
    IDateTimeProvider clock)
    : ICommandHandler<ResetMyDataCommand, Result>
{
    public async ValueTask<Result> Handle(ResetMyDataCommand command, CancellationToken cancellationToken)
    {
        // Defense-in-depth: AuthorizationBehavior normally checks the
        // IAuthenticatedRequest marker, but we don't take a hard dependency on the
        // pipeline being configured (mirrors DeleteAccountCommandHandler).
        if (!currentUser.UserId.HasValue)
            return Result.Failure(
                DomainError.Validation(
                    "Dev.NotAuthenticated",
                    "Inloggning krävs för att återställa dina data."));

        var userId = currentUser.UserId.Value;

        // Active JobSeeker only (no IgnoreQueryFilters) — a soft-deleted account has
        // nothing meaningful to reset. Tolerant: if there is no seeker yet, the dev
        // simply has nothing to clear → Success (idempotent).
        var jobSeeker = await db.JobSeekers
            .FirstOrDefaultAsync(js => js.UserId == userId, cancellationToken);

        if (jobSeeker is null)
            return Result.Success();

        // CVs — soft-delete via the aggregate's own method (cascades to Versions).
        // Global query filter already excludes any already soft-deleted rows.
        var resumes = await db.Resumes
            .Where(r => r.JobSeekerId == jobSeeker.Id)
            .Include(r => r.Versions)
            .ToListAsync(cancellationToken);
        foreach (var resume in resumes)
            resume.SoftDelete(clock);

        // Parsed-CV staging artifacts — soft-delete via Discard (sets DeletedAt).
        var parsedResumes = await db.ParsedResumes
            .Where(p => p.JobSeekerId == jobSeeker.Id)
            .ToListAsync(cancellationToken);
        foreach (var parsed in parsedResumes)
            parsed.Discard(clock);

        // "Sökta annonser" — saved bookmarks (hard-delete, raise unsaved event first).
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

        // Reset stated match preferences → Empty so hasStatedDesiredOccupation
        // becomes false and the welcome modal re-triggers (tracked mutation).
        jobSeeker.UpdateMatchPreferences(MatchPreferences.Empty, clock);

        // SaveChanges happens via UnitOfWorkBehavior — atomic across all the above.
        return Result.Success();
    }
}
