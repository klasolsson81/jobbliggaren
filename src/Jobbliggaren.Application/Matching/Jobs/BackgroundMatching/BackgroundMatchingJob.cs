using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Notifications;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Grading;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Matching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Application.Matching.Jobs.BackgroundMatching;

/// <summary>
/// ADR 0080 Vag 4, ADR 0161 (#2106) — the background-matching run, every five minutes. For every
/// CONSENTING user (opt-in ON, not withdrawn — GDPR Art. 6/7), it scores the ads ingested since that
/// user's <see cref="Domain.JobSeekers.JobSeeker.LastMatchScanAt"/> watermark against their DEK-free
/// profile and persists a <see cref="UserJobAdMatch"/> for each notifiable grade (Good/Strong/Top —
/// Basic / no-grade is the honest floor, never persisted). Then <see cref="TopMatchMailDispatcher"/>
/// mails the due Top matches in the same run. NO AI/LLM (ADR 0071).
/// <para>
/// <b>DEK-free:</b> the profile comes from <see cref="IMatchProfileBuilder.BuildFullForUserIdAsync"/>
/// (plaintext PreferredSkills + LatestRole, no per-user KMS in the hot loop), and it is built only for a
/// user who has a new candidate ad (security-auditor V6).
/// </para>
/// <para>
/// <b>One candidate read per run.</b> The run reads the Active ads ingested since the oldest due
/// watermark once, and each user takes the part newer than their own watermark. A user with nothing
/// new is skipped entirely: no profile build, no write.
/// </para>
/// <para>
/// <b>The watermark trails the clock by <see cref="IngestSettleWindow"/>.</b> <c>JobAd.Import</c> stamps
/// <c>CreatedAt</c> before the ad commits, so an ad stamped before a run's read can commit after it.
/// Advancing to "now" would skip such an ad for good. Advancing to now minus the window re-reads the
/// last two minutes of ads every run (the existing-pair check drops the ones already matched) and
/// misses an ad only if its commit took longer than the window. Lapse: if an ingest path starts
/// stamping in one transaction and committing in a later one, or the measured stamp-to-commit time
/// approaches a tenth of the window, derive the window again.
/// </para>
/// <para>
/// <b>Idempotent:</b> the per-user watermark plus the <c>UNIQUE(UserId, JobAdId)</c> spine mean a re-run
/// never creates a second row, and the dispatcher only sends Pending rows. The watermark advance and the
/// match inserts commit in ONE <see cref="IAppDbContext.SaveChangesAsync"/>: a crash mid-scan never
/// either drops matches or loses the advance.
/// </para>
/// <para>
/// <b>Cold start and re-consent:</b> the scan never reaches further back than <see cref="ColdStartDays"/>,
/// whether the user was never scanned or turned consent back on after a long pause. The existing
/// active corpus stays reachable through the /jobb grade filter.
/// </para>
/// <para>
/// <b>Child scope per user (#751):</b> every user's scan runs in its own DI scope, so a failed attempt's
/// tracked entities die with it instead of riding into the next user's save. Per-user failure is
/// isolated: one user's exception does not abort the run, and that user's watermark does not move.
/// </para>
/// </summary>
public sealed partial class BackgroundMatchingJob(
    IServiceScopeFactory scopeFactory,
    TopMatchMailDispatcher topMatchMail,
    IDateTimeProvider clock,
    ILogger<BackgroundMatchingJob> logger)
{
    /// <summary>The furthest back a scan reaches: seven days of ingested ads.</summary>
    public const int ColdStartDays = 7;

    /// <summary>How far the watermark trails the run's clock. See the class docs.</summary>
    public static readonly TimeSpan IngestSettleWindow = TimeSpan.FromMinutes(2);

    /// <summary>
    /// How long the scan phase starts new users. A user already started finishes. Users left over keep
    /// their watermark.
    /// </summary>
    public static readonly TimeSpan ScanBudget = TimeSpan.FromMinutes(3);

    /// <summary>
    /// How long after an ad's ingest its Top match is mailed at the latest, while a Worker slot is free,
    /// no stale lock holds the job, the run that first reads the ad reaches the user in both phases (no
    /// budget, cap or earlier send failure cuts it) and the provider accepts the mail.
    /// </summary>
    public static readonly TimeSpan TopMatchMailBound = TimeSpan.FromMinutes(15);

    // A match row is created after its ad commits, so every existing match for an ad newer than a
    // user's watermark is newer than that watermark too. The day of margin covers a clock step.
    private static readonly TimeSpan ExistingMatchMargin = TimeSpan.FromDays(1);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        // Read before the candidate read, so the advanced watermark is never later than what was read.
        var now = clock.UtcNow;
        var floor = now.AddDays(-ColdStartDays);

        // The CONSENTING set (GDPR Art. 6/7), steady users first and never-scanned users last: a
        // cold-start user's matches are not "within minutes" by definition, so they wait if the budget
        // runs out. The null test is explicit because providers order nulls differently.
        List<DueUser> due;
        List<CandidateAd> candidates;
        await using (var readScope = scopeFactory.CreateAsyncScope())
        {
            var db = readScope.ServiceProvider.GetRequiredService<IAppDbContext>();
            due = await db.JobSeekers
                .Where(NotificationConsent.BackgroundMatch.Criteria)
                .OrderBy(seeker => seeker.LastMatchScanAt == null)
                .ThenBy(seeker => seeker.LastMatchScanAt)
                .Select(seeker => new DueUser(seeker.UserId, seeker.LastMatchScanAt))
                .ToListAsync(cancellationToken);

            candidates = due.Count == 0
                ? []
                : await ReadCandidatesAsync(db, due.Min(user => Since(user.LastMatchScanAt, floor)), cancellationToken);
        }

        var scanned = 0;
        var totalMatches = 0;
        foreach (var user in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (clock.UtcNow - now >= ScanBudget)
            {
                LogScanBudgetSpent(logger, scanned, due.Count);
                break;
            }

            var since = Since(user.LastMatchScanAt, floor);
            if (!candidates.Exists(ad => ad.CreatedAt > since))
                continue;

            try
            {
                // ADR 0146 — the row carries an xmin token, so a write committed between this user's load
                // and the scan's commit (a withdrawal, a settings save, a /jobb visit) rolls the commit back
                // whole. One more attempt reads the row again and re-checks consent on it.
                try
                {
                    totalMatches += await ScanUserInOwnScopeAsync(user.UserId, candidates, now, cancellationToken);
                }
                catch (DbUpdateConcurrencyException)
                {
                    totalMatches += await ScanUserInOwnScopeAsync(user.UserId, candidates, now, cancellationToken);
                }
            }
            catch (DbUpdateConcurrencyException)
            {
                // Two conflicts in a row: the row is busy. The watermark did not move, so the next run
                // picks the user up again.
                LogUserDeferred(logger, user.UserId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Per-user isolation (TD-25): one user's failure must not abort the run. A cancellation is
                // not swallowed, so a host shutdown stops the run promptly.
                LogUserFailed(logger, ex, user.UserId);
            }

            scanned++;
        }

        if (totalMatches > 0)
            LogScanned(logger, scanned, due.Count, totalMatches);
        else
            LogScannedNothingNew(logger, scanned, due.Count);

        await topMatchMail.DispatchAsync(MailPacingRule.TopMatch, cancellationToken);
    }

    private static DateTimeOffset Since(DateTimeOffset? lastScan, DateTimeOffset floor) =>
        lastScan is { } last && last > floor ? last : floor;

    // Filter by CreatedAt (INGEST time), not PublishedAt (ADR 0080 Beslut 2). PublishedAt is
    // JobTech-supplied and can be backdated, so filtering on it would skip a late-ingested ad for good.
    private static Task<List<CandidateAd>> ReadCandidatesAsync(
        IAppDbContext db, DateTimeOffset since, CancellationToken ct) =>
        db.JobAds
            .AsNoTracking()
            .Where(ad => ad.Status == JobAdStatus.Active && ad.CreatedAt > since)
            .Select(ad => new CandidateAd(ad.Id, ad.CreatedAt))
            .ToListAsync(ct);

    // Own DI scope per attempt (#751) → own IAppDbContext (and profile builder / scorer over that same
    // context), so a failed attempt's tracked entities die with it.
    private async Task<int> ScanUserInOwnScopeAsync(
        Guid userId, IReadOnlyList<CandidateAd> candidates, DateTimeOffset now, CancellationToken ct)
    {
        await using var userScope = scopeFactory.CreateAsyncScope();
        var sp = userScope.ServiceProvider;
        return await ScanUserAsync(
            sp.GetRequiredService<IAppDbContext>(),
            sp.GetRequiredService<IMatchProfileBuilder>(),
            sp.GetRequiredService<IMatchScorer>(),
            userId, candidates, now, ct);
    }

    // The per-user unit. The candidate set was read once for the run; this method takes the part newer
    // than the user's own watermark, read again on the tracked row.
    private async Task<int> ScanUserAsync(
        IAppDbContext db,
        IMatchProfileBuilder profileBuilder,
        IMatchScorer scorer,
        Guid userId,
        IReadOnlyList<CandidateAd> candidates,
        DateTimeOffset now,
        CancellationToken ct)
    {
        // Tracked load — the watermark advance must persist in the SAME unit of work as the inserts.
        var jobSeeker = await db.JobSeekers
            .FirstOrDefaultAsync(js => js.UserId == userId, ct);
        if (jobSeeker is null)
            return 0;

        // ADR 0146 — consent is decided on the row this attempt loaded, not on the run's due set.
        if (!NotificationConsent.BackgroundMatch.IsSatisfiedBy(jobSeeker))
            return 0;

        var since = Since(jobSeeker.LastMatchScanAt, now.AddDays(-ColdStartDays));
        var newAdIds = candidates.Where(ad => ad.CreatedAt > since).Select(ad => ad.Id).ToList();
        if (newAdIds.Count == 0)
            return 0;

        var scannedThrough = now - IngestSettleWindow;
        var profile = await profileBuilder.BuildFullForUserIdAsync(userId, ct);

        // SSYK gate: without a stated occupation no ad can earn a grade. Advance the watermark and
        // return, committed in one SaveChanges.
        if (profile.Fast.SsykGroupConceptIds.Count == 0)
        {
            jobSeeker.AdvanceMatchScan(scannedThrough, clock);
            await db.SaveChangesAsync(ct);
            return 0;
        }

        var scores = await scorer.ScoreFullBatchAsync(newAdIds, profile, ct);

        // Idempotency backstop: UNIQUE(UserId, JobAdId) prevents a second row, and this skip keeps the
        // insert batch from throwing on the re-read window. Loaded client-side to avoid the
        // strongly-typed-VO Contains translation trap (memory ef_strongly_typed_vo_contains).
        var existingCutoff = since - ExistingMatchMargin;
        var existingJobAdIds = (await db.UserJobAdMatches
            .Where(m => m.UserId == userId && m.CreatedAt > existingCutoff)
            .Select(m => m.JobAdId)
            .ToListAsync(ct))
            .ToHashSet();

        var matchCount = 0;
        foreach (var (jobAdId, scored) in scores)
        {
            if (existingJobAdIds.Contains(jobAdId))
                continue;

            // FULL grade. #300 PR-4 (ADR 0084 §F4): SsykIsRelated grades Related, which ToNotifiable maps
            // to null; the scan builds with includeRelated=false, so it never fires here.
            var grade = MatchGradeCalculator.Grade(scored.Score, scored.SsykIsRelated);
            if (grade is null || ToNotifiable(grade.Value) is not { } notifiable)
                continue;

            // #477 Low 2 — persist the SkillOverlap evidence, truncated to the aggregate's cap so a
            // pathological intersection never fails Create. The scorer already deduped and ordered it.
            var evidence = scored.MatchedSkillConceptIds.Count > UserJobAdMatch.MaxMatchedSkills
                ? scored.MatchedSkillConceptIds.Take(UserJobAdMatch.MaxMatchedSkills).ToList()
                : scored.MatchedSkillConceptIds;
            var created = UserJobAdMatch.Create(userId, jobAdId, notifiable, evidence, clock);
            if (created.IsSuccess)
            {
                db.UserJobAdMatches.Add(created.Value);
                matchCount++;
            }
        }

        // HARD atomicity invariant: the new matches AND the watermark advance commit together.
        jobSeeker.AdvanceMatchScan(scannedThrough, clock);
        await db.SaveChangesAsync(ct);
        return matchCount;
    }

    // Map the computed grade to the persisted notifiable subset. Basic → null (the honest floor, ADR 0080
    // Beslut 1).
    private static NotifiableMatchGrade? ToNotifiable(MatchGrade grade) => grade switch
    {
        MatchGrade.Good => NotifiableMatchGrade.Good,
        MatchGrade.Strong => NotifiableMatchGrade.Strong,
        MatchGrade.Top => NotifiableMatchGrade.Top,
        _ => null,
    };

    private sealed record DueUser(Guid UserId, DateTimeOffset? LastMatchScanAt);

    private sealed record CandidateAd(JobAdId Id, DateTimeOffset CreatedAt);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "BackgroundMatchingJob: {Scanned} of {Due} consenting users scanned, no new matches")]
    private static partial void LogScannedNothingNew(ILogger logger, int scanned, int due);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "BackgroundMatchingJob: {Scanned} of {Due} consenting users scanned, {TotalMatches} new matches persisted")]
    private static partial void LogScanned(ILogger logger, int scanned, int due, int totalMatches);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "BackgroundMatchingJob: the scan budget is spent after {Scanned} of {Due} users; the rest wait for the next run")]
    private static partial void LogScanBudgetSpent(ILogger logger, int scanned, int due);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "BackgroundMatchingJob: user {UserId}'s row changed twice during the scan; deferred to the next run")]
    private static partial void LogUserDeferred(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "BackgroundMatchingJob: scan failed for user {UserId}; isolated, retried next run")]
    private static partial void LogUserFailed(ILogger logger, Exception ex, Guid userId);
}
