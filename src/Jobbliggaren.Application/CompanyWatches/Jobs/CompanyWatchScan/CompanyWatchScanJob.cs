using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Application.CompanyWatches.Jobs.CompanyWatchScan;

/// <summary>
/// ADR 0087 D5 (#311 PR-4) — the scheduled company-follow scan. For every CONSENTING user (the
/// SEPARATE company-follow opt-in ON, not withdrawn — GDPR Art. 6/7), it finds the ads published
/// since that user's <see cref="Domain.JobSeekers.JobSeeker.LastCompanyWatchScanAt"/> watermark
/// whose <c>organization_number</c> is one of the user's ACTIVE watched org.nrs, and persists a
/// <see cref="FollowedCompanyAdHit"/> for each. Registered as a Hangfire RecurringJob in the same
/// nightly window as <c>BackgroundMatchingJob</c>, with its OWN watermark. NO AI/LLM, NO skill
/// scorer (a company hit requires only org.nr membership — ADR 0087 D5).
/// <para>
/// <b>Dedicated job, NOT a fold into <c>BackgroundMatchingJob</c> (ADR 0087 D5, Alt Z — rejected):</b>
/// skill-scoring and watched-org.nr-membership are two independent change reasons (SRP). This job
/// never invokes the scorer or reads a profile — it is a pure set-membership query.
/// </para>
/// <para>
/// <b>Idempotent BY DESIGN:</b> the per-user <see cref="Domain.JobSeekers.JobSeeker.LastCompanyWatchScanAt"/>
/// high-water-mark + the <c>UNIQUE(UserId, JobAdId, CompanyWatchId)</c> dedup spine mean a re-run
/// never re-notifies. The watermark advance and the hit inserts commit in ONE
/// <see cref="IAppDbContext.SaveChangesAsync"/> (a single transaction) — the HARD atomicity
/// invariant (parity <c>BackgroundMatchingJob</c>): a crash mid-scan must never either re-notify
/// (advance lost) or silently drop hits (advance committed, inserts rolled back).
/// </para>
/// <para>
/// <b>Cold-start:</b> a never-scanned user seeds a 7-day ad floor (<see cref="ColdStartDays"/>) so
/// the first run is bounded. Per-user failure is isolated (one user's exception does not abort the
/// batch — TD-25 pattern).
/// </para>
/// <para>
/// <b>GDPR (ADR 0087 D8 / CLAUDE.md §5 — highest-priority guard):</b> a sole-prop (enskild firma)
/// org.nr can equal a personnummer, so this job NEVER logs an org.nr (its LoggerMessages carry only
/// counts + opaque user ids). It does NOT dispatch email — the reused <c>DigestDispatchJob</c>
/// surfaces the PUBLIC company NAME (never the org.nr) at send time.
/// </para>
/// </summary>
public sealed partial class CompanyWatchScanJob(
    IAppDbContext db,
    IDateTimeProvider clock,
    ILogger<CompanyWatchScanJob> logger)
{
    // Cold-start ad floor: a user with no prior scan is matched only against ads ingested in the
    // last week (avoids a multi-hour first run + a first-digest spam burst). Parity BackgroundMatchingJob.
    private const int ColdStartDays = 7;
    private const int ProgressLogEvery = 50;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        // The CONSENTING set (GDPR Art. 6/7): the SEPARATE company-follow opt-in ON and not
        // withdrawn. A withdrawal stops scan/dispatch immediately. Default OFF → the set is small.
        var optedInUserIds = await db.JobSeekers
            .Where(js => js.Preferences.FollowedCompanyNotificationsEnabled
                         && js.Preferences.FollowedCompanyNotificationConsentWithdrawnAt == null)
            .Select(js => js.UserId)
            .ToListAsync(cancellationToken);

        LogOptedIn(logger, optedInUserIds.Count);

        var processedUsers = 0;
        var totalHits = 0;
        foreach (var userId in optedInUserIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                totalHits += await ScanUserAsync(userId, now, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Per-user isolation (TD-25): one user's failure must not abort the batch. The
                // watermark is NOT advanced on failure (one SaveChanges; a throw rolls back both the
                // inserts and the advance) → the user is re-scanned cleanly next run. A cancellation
                // propagates (host shutdown / cron-timeout) — not mis-logged as a user failure.
                LogUserFailed(logger, ex, userId);
            }

            processedUsers++;
            if (processedUsers % ProgressLogEvery == 0)
                LogProgress(logger, processedUsers, optedInUserIds.Count);
        }

        LogComplete(logger, processedUsers, totalHits);
    }

    private async Task<int> ScanUserAsync(Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        // Tracked load — the watermark advance must persist in the SAME unit of work as the hit
        // inserts (atomicity).
        var jobSeeker = await db.JobSeekers
            .FirstOrDefaultAsync(js => js.UserId == userId, ct);
        if (jobSeeker is null)
            return 0; // consent row without a JobSeeker (shouldn't happen) — nothing to scan.

        // The user's ACTIVE follows (the query filter excludes unfollowed/soft-deleted rows). The
        // active-partial UNIQUE guarantees ≤1 active watch per org.nr, so the org.nr → watch map is
        // well-defined. The set is small (a user's follow list) → load + map in memory (the org.nr
        // VO's .Value is client-side; never a strongly-typed-VO Contains against the DB).
        var activeWatches = await db.CompanyWatches
            .AsNoTracking()
            .Where(w => w.UserId == userId)
            .ToListAsync(ct);

        if (activeWatches.Count == 0)
        {
            // No follows → nothing to scan. Advance the watermark (scanned through now, 0 hits) so a
            // later follow only catches FUTURE ads, and commit atomically.
            jobSeeker.AdvanceCompanyWatchScan(now, clock);
            await db.SaveChangesAsync(ct);
            return 0;
        }

        var watchIdByOrgNr = activeWatches.ToDictionary(
            w => w.OrganizationNumber.Value, w => w.Id, StringComparer.Ordinal);
        // IReadOnlyList<string> (not List<string>) so the org.nr membership uses the LINQ
        // Enumerable.Contains overload — EF translates it to SQL IN over the nullable shadow column
        // (parity the D6 ApplyFilter employer filter; a List<string>.Contains would reject the
        // string? column arg at compile time).
        IReadOnlyList<string> watchedOrgNrs = watchIdByOrgNr.Keys.ToList();

        var since = jobSeeker.LastCompanyWatchScanAt ?? now.AddDays(-ColdStartDays);

        // Filter by CreatedAt (INGEST time), not PublishedAt (parity BackgroundMatchingJob — the
        // watermark advances to clock-now, and CreatedAt is the monotonic ingest timestamp, so
        // CreatedAt > since catches EVERY ad ingested since the last scan). The org.nr IN-membership
        // uses the STORED generated shadow column (EF.Property — the same translation-safe pattern
        // as the D6 employer filter; org.nr on job_ads is a plain string, not a VO). Project the id +
        // its org.nr so we can map back to the originating watch client-side (no join).
        var newAds = await db.JobAds
            .AsNoTracking()
            .Where(j => j.Status == JobAdStatus.Active
                        && j.CreatedAt > since
                        && watchedOrgNrs.Contains(EF.Property<string?>(j, "OrganizationNumber")))
            .Select(j => new { j.Id, OrgNr = EF.Property<string?>(j, "OrganizationNumber") })
            .ToListAsync(ct);

        var hitCount = 0;
        if (newAds.Count > 0)
        {
            // Idempotency backstop: UNIQUE(UserId, JobAdId, CompanyWatchId) prevents re-notification,
            // but skip already-persisted triples so the insert batch never throws on a window overlap
            // / re-run. Load the user's existing (jobAdId, watchId) pairs client-side (bounded by
            // their accumulated hits) — avoids the strongly-typed-VO Contains translation trap.
            var existing = (await db.FollowedCompanyAdHits
                    .IgnoreQueryFilters()
                    .Where(h => h.UserId == userId)
                    .Select(h => new { h.JobAdId, h.CompanyWatchId })
                    .ToListAsync(ct))
                .Select(x => (x.JobAdId, x.CompanyWatchId))
                .ToHashSet();

            foreach (var ad in newAds)
            {
                // The ad matched the IN of non-null watched org.nrs, so OrgNr is present and maps to
                // exactly one active watch; TryGetValue is defense-in-depth against a NULL leak.
                if (ad.OrgNr is null || !watchIdByOrgNr.TryGetValue(ad.OrgNr, out var watchId))
                    continue;

                if (existing.Contains((ad.Id, watchId)))
                    continue;

                var created = FollowedCompanyAdHit.Create(userId, ad.Id, watchId, clock);
                if (created.IsSuccess)
                {
                    db.FollowedCompanyAdHits.Add(created.Value);
                    hitCount++;
                }
            }
        }

        // HARD atomicity invariant: the new hits AND the watermark advance commit together in ONE
        // SaveChanges (transaction). Never split them.
        jobSeeker.AdvanceCompanyWatchScan(now, clock);
        await db.SaveChangesAsync(ct);

        return hitCount;
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "CompanyWatchScanJob: {Count} consenting users to scan")]
    private static partial void LogOptedIn(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "CompanyWatchScanJob: {Processed}/{Total} users scanned")]
    private static partial void LogProgress(ILogger logger, int processed, int total);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "CompanyWatchScanJob: scan failed for user {UserId} — isolated, will retry next run")]
    private static partial void LogUserFailed(ILogger logger, Exception ex, Guid userId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "CompanyWatchScanJob: done — {Processed} users scanned, {TotalHits} new follow hits persisted")]
    private static partial void LogComplete(ILogger logger, int processed, int totalHits);
}
