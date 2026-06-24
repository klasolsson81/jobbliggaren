using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Grading;
using Jobbliggaren.Application.Matching.Notifications;
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
/// <b>Top-direct email (PR-4b):</b> a new Top match is emailed directly in this run (Strong
/// accumulates into the cadence digest; Good is in-app only). The send runs AFTER the atomic
/// commit (the matches persist even if the email fails) with a claim-then-send spine
/// (<c>Pending → MarkQueued</c> + commit → send → <c>MarkSent</c> + commit) so a retry never
/// double-sends. A send failure leaves the row Queued (never re-sent): the deliberate "never
/// double-email &gt; never miss" trade-off (the stranded-Queued reaper is TD-114). The body is
/// PII-free (public title/company + named grade label, never a number — Goodhart).
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
    IEmailSender emailSender,
    IUserAccountService userAccounts,
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
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Per-user isolation (TD-25): one user's failure must not abort the batch. The
                // watermark is NOT advanced on failure (one SaveChanges; a throw rolls back both
                // the inserts and the advance) → the user is re-scanned cleanly next run. A
                // cancellation (OperationCanceledException) is NOT swallowed — it propagates so
                // the host shutdown / cron-timeout stops the scan promptly (not mis-logged as a
                // user failure).
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
        // Project the candidate ads' PUBLIC title/company alongside the id in the SAME windowed
        // round-trip — they feed the Top-direct email body (PR-4b) with no extra query and no
        // strongly-typed-VO Contains (the CreatedAt window is the filter, not an id set). Title +
        // Company.Name are owned columns on job_ads (no join).
        var newAds = await db.JobAds
            .AsNoTracking()
            .Where(j => j.Status == JobAdStatus.Active && j.CreatedAt > since)
            .Select(j => new { j.Id, j.Title, Company = j.Company.Name })
            .ToListAsync(ct);
        var newAdIds = newAds.Select(a => a.Id).ToList();
        var adPublicById = newAds.ToDictionary(a => a.Id, a => (a.Title, a.Company));

        var matchCount = 0;
        var topDispatch = new List<(UserJobAdMatch Match, MatchNotificationItem Item)>();
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

                    // Top is the only directly-dispatched grade (Strong → digest, Good → in-app
                    // only). Collect the row + its PII-free email item now (the entity is needed to
                    // drive MarkQueued/MarkSent after the commit). adPublicById always has the ad
                    // (the score came from this window); TryGetValue is defense-in-depth.
                    if (notifiable == NotifiableMatchGrade.Top
                        && adPublicById.TryGetValue(jobAdId, out var ad))
                    {
                        topDispatch.Add((created.Value, new MatchNotificationItem(
                            ad.Title, ad.Company, notifiable.ToSwedishLabel())));
                    }
                }
            }
        }

        // HARD atomicity invariant: the new matches AND the watermark advance commit together in
        // ONE SaveChanges (transaction). Never split them.
        jobSeeker.AdvanceMatchScan(now, clock);
        await db.SaveChangesAsync(ct);

        // Top-direct dispatch (PR-4b) runs AFTER the atomic commit so the persisted matches survive
        // an email failure (the send is not transactional). It self-isolates internally (logs, does
        // not rethrow ordinary failures), so a send error neither rolls back the matches nor aborts
        // the user's scan — matchCount is still returned honestly.
        if (topDispatch.Count > 0)
            await DispatchTopDirectAsync(userId, topDispatch, ct);

        return matchCount;
    }

    // One focused Direct email per new Top match (parity the singular template copy). The claim
    // (Pending → Queued) commits BEFORE the send so a Hangfire retry / re-run never re-sends (the
    // scan only re-touches Pending). Per-match isolation: a failure on one Top match neither blocks
    // the others nor rolls back the persisted matches; the stranded Queued row is the deliberate
    // "never double-email > never miss" trade-off (TD-114 reaper). ADR 0080 Vag 4 PR-4b.
    private async Task DispatchTopDirectAsync(
        Guid userId,
        IReadOnlyList<(UserJobAdMatch Match, MatchNotificationItem Item)> topMatches,
        CancellationToken ct)
    {
        var toEmail = await userAccounts.GetEmailAsync(userId, ct);
        if (string.IsNullOrWhiteSpace(toEmail))
        {
            // Orphan consent row without an account email — skip (rows stay Pending; the next scan
            // won't re-pick them, but a Top-email without a recipient is a no-op either way).
            LogTopNoEmail(logger, userId);
            return;
        }

        foreach (var (match, item) in topMatches)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // Claim before the send (the idempotency spine). MarkQueued requires Pending; the
                // row was just created Pending, so a Failure is structurally impossible — guard
                // anyway and skip rather than send an unclaimed row.
                if (match.MarkQueued().IsFailure)
                    continue;
                await db.SaveChangesAsync(ct);

                var content = new MatchNotificationEmail(
                    MatchNotificationKind.Direct, null, [item], 1);
                await emailSender.SendMatchNotificationEmailAsync(toEmail, content, ct);

                match.MarkSent(clock);
                await db.SaveChangesAsync(ct);
                LogTopSent(logger, userId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Send/commit failed after the claim → this row stays Queued (never re-sent). Other
                // Top matches for this user still get their own attempt.
                LogTopSendFailed(logger, ex, userId);
            }
        }
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

    [LoggerMessage(Level = LogLevel.Information,
        Message = "BackgroundMatchingJob: Top-direct skipped for user {UserId} — no account email")]
    private static partial void LogTopNoEmail(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "BackgroundMatchingJob: Top-direct email sent for user {UserId}")]
    private static partial void LogTopSent(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "BackgroundMatchingJob: Top-direct send failed for user {UserId} — match persisted, row left Queued")]
    private static partial void LogTopSendFailed(ILogger logger, Exception ex, Guid userId);
}
