using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Grading;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.Matching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Application.Matching.Jobs.BackgroundMatching;

/// <summary>
/// ADR 0080 Vag 4 PR-3 (Beslut 2/3) — the scheduled background-matching scan. For every
/// CONSENTING user (opt-in ON, not withdrawn — GDPR Art. 6/7), it scores the ads published
/// since that user's <see cref="Domain.JobSeekers.JobSeeker.LastMatchScanAt"/> watermark
/// against their DEK-free profile and persists a <see cref="UserJobAdMatch"/> for each
/// notifiable grade (Good/Strong/Top — Basic / no-grade is the honest floor, never persisted).
/// Registered as a Hangfire RecurringJob (~03:20 UTC, after the snapshot settles, before the
/// hard-delete window), mirroring <c>DetectGhostedApplicationsJob</c>. NO AI/LLM (ADR 0071).
/// <para>
/// <b>DEK-free (the STEG 3 enabler):</b> the profile comes from
/// <see cref="IMatchProfileBuilder.BuildFullForUserIdAsync"/> (plaintext PreferredSkills +
/// LatestRole, no per-user KMS in the hot loop). The Worker is the FIRST place the FULL
/// <see cref="MatchGradeCalculator.Grade(FullMatchScore)"/> runs, so it is the first place
/// <see cref="NotifiableMatchGrade.Top"/> is produced (the page-sort path is Fast-band only).
/// </para>
/// <para>
/// <b>Idempotent BY DESIGN:</b> the per-user <c>LastMatchScanAt</c> high-water-mark + the
/// <c>UNIQUE(UserId, JobAdId)</c> dedup spine mean a re-run never re-notifies. The watermark
/// advance and the match inserts commit in ONE <see cref="IAppDbContext.SaveChangesAsync"/>
/// (a single transaction) — the HARD atomicity invariant: a crash mid-scan must never either
/// re-notify (advance lost) or silently drop matches (advance committed, inserts rolled back).
/// </para>
/// <para>
/// <b>Cold-start:</b> a never-scanned user seeds a 7-day ad floor (<see cref="ColdStartDays"/>)
/// so the first run is bounded — no multi-hour scan and no first-digest spam burst. Per-user
/// failure is isolated (one user's exception does not abort the batch — TD-25 pattern).
/// </para>
/// </summary>
public sealed partial class BackgroundMatchingJob(
    IAppDbContext db,
    IMatchProfileBuilder profileBuilder,
    IMatchScorer scorer,
    IDateTimeProvider clock,
    ILogger<BackgroundMatchingJob> logger)
{
    // Cold-start ad floor: a user with no prior scan is matched only against ads published in
    // the last week, not the whole corpus (avoids a multi-hour first run + a spam burst).
    private const int ColdStartDays = 7;
    private const int ProgressLogEvery = 50;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        // The CONSENTING set (GDPR Art. 6/7): opt-in ON and not withdrawn. A withdrawal stops
        // dispatch immediately (the filter excludes withdrawn users). Default OFF → most rows
        // are excluded; the set is small, so a per-user loop is fine for the $16-VPS MVP.
        var optedInUserIds = await db.JobSeekers
            .Where(js => js.Preferences.BackgroundMatchNotificationsEnabled
                         && js.Preferences.NotificationConsentWithdrawnAt == null)
            .Select(js => js.UserId)
            .ToListAsync(cancellationToken);

        LogOptedIn(logger, optedInUserIds.Count);

        var processedUsers = 0;
        var totalMatches = 0;
        foreach (var userId in optedInUserIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                totalMatches += await ScanUserAsync(userId, now, cancellationToken);
            }
            catch (Exception ex)
            {
                // Per-user isolation (TD-25): one user's failure must not abort the batch. The
                // watermark is NOT advanced on failure (one SaveChanges; a throw rolls back both
                // the inserts and the advance) → the user is re-scanned cleanly next run.
                LogUserFailed(logger, ex, userId);
            }

            processedUsers++;
            if (processedUsers % ProgressLogEvery == 0)
                LogProgress(logger, processedUsers, optedInUserIds.Count);
        }

        LogComplete(logger, processedUsers, totalMatches);
    }

    private async Task<int> ScanUserAsync(Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        // Tracked load — the watermark advance must persist in the SAME unit of work as the
        // match inserts (atomicity).
        var jobSeeker = await db.JobSeekers
            .FirstOrDefaultAsync(js => js.UserId == userId, ct);
        if (jobSeeker is null)
            return 0; // consent row without a JobSeeker (shouldn't happen) — nothing to scan.

        var profile = await profileBuilder.BuildFullForUserIdAsync(userId, ct);

        // SSYK gate (parity the list/batch paths): without a stated occupation no ad can earn a
        // grade. Advance the watermark (scanned through now, 0 matches) so we don't re-scan, and
        // return — committed atomically (a single SaveChanges).
        if (profile.Fast.SsykGroupConceptIds.Count == 0)
        {
            jobSeeker.AdvanceMatchScan(now, clock);
            await db.SaveChangesAsync(ct);
            return 0;
        }

        var since = jobSeeker.LastMatchScanAt ?? now.AddDays(-ColdStartDays);

        // Filter by CreatedAt (INGEST time), not PublishedAt (ADR 0080 Beslut 2 — "CreatedAt
        // for ingest-time"). The watermark advances to clock-now, and CreatedAt is the monotonic
        // ingest timestamp, so CreatedAt > since catches EVERY ad ingested since the last scan.
        // PublishedAt is JobTech-supplied and can be backdated — filtering on it would
        // permanently skip a late-ingested ad whose PublishedAt is older than the now-advanced
        // watermark (security-auditor correctness flag 2026-06-24). Cold-start: a never-scanned
        // user is matched only against ads ingested in the last 7 days (ColdStartDays) — bounded
        // first run, no spam burst; the existing active corpus stays reachable via the /jobb
        // grade-filter.
        var newAdIds = await db.JobAds
            .AsNoTracking()
            .Where(j => j.Status == JobAdStatus.Active && j.CreatedAt > since)
            .Select(j => j.Id)
            .ToListAsync(ct);

        var matchCount = 0;
        if (newAdIds.Count > 0)
        {
            var scores = await scorer.ScoreFullBatchAsync(newAdIds, profile, ct);

            // Idempotency backstop: UNIQUE(UserId, JobAdId) prevents re-notification, but skip
            // already-persisted pairs so the insert batch never throws on a window overlap /
            // re-run. Load the user's existing match ids (bounded by their accumulated matches)
            // client-side — avoids the strongly-typed-VO Contains translation trap
            // (memory ef_strongly_typed_vo_contains).
            var existingJobAdIds = (await db.UserJobAdMatches
                .IgnoreQueryFilters()
                .Where(m => m.UserId == userId)
                .Select(m => m.JobAdId)
                .ToListAsync(ct))
                .ToHashSet();

            foreach (var (jobAdId, score) in scores)
            {
                if (existingJobAdIds.Contains(jobAdId))
                    continue;

                // FULL grade (can reach Top — the Worker is the first producer). Map to the
                // notifiable subset; Basic / no-grade is the honest floor (never persisted).
                var grade = MatchGradeCalculator.Grade(score);
                if (grade is null || ToNotifiable(grade.Value) is not { } notifiable)
                    continue;

                // MatchedSkillConceptIds is left empty in the MVP scan — the SkillOverlap
                // dimension surfaces Display labels, not concept-ids; the in-app surface (PR-5)
                // shows the count + grade, and the evidence enrichment is a follow-up.
                var created = UserJobAdMatch.Create(userId, jobAdId, notifiable, [], clock);
                if (created.IsSuccess)
                {
                    db.UserJobAdMatches.Add(created.Value);
                    matchCount++;
                }
            }
        }

        // HARD atomicity invariant: the new matches AND the watermark advance commit together in
        // ONE SaveChanges (transaction). Never split them.
        jobSeeker.AdvanceMatchScan(now, clock);
        await db.SaveChangesAsync(ct);
        return matchCount;
    }

    // Map the computed grade to the persisted notifiable subset. Basic → null (not notifiable —
    // the honest floor, ADR 0080 Beslut 1); the calculator's null (no grade) is handled before.
    private static NotifiableMatchGrade? ToNotifiable(MatchGrade grade) => grade switch
    {
        MatchGrade.Good => NotifiableMatchGrade.Good,
        MatchGrade.Strong => NotifiableMatchGrade.Strong,
        MatchGrade.Top => NotifiableMatchGrade.Top,
        _ => null,
    };

    [LoggerMessage(Level = LogLevel.Information,
        Message = "BackgroundMatchingJob: {Count} consenting users to scan")]
    private static partial void LogOptedIn(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "BackgroundMatchingJob: {Processed}/{Total} users scanned")]
    private static partial void LogProgress(ILogger logger, int processed, int total);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "BackgroundMatchingJob: scan failed for user {UserId} — isolated, will retry next run")]
    private static partial void LogUserFailed(ILogger logger, Exception ex, Guid userId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "BackgroundMatchingJob: done — {Processed} users scanned, {TotalMatches} new matches persisted")]
    private static partial void LogComplete(ILogger logger, int processed, int totalMatches);
}
