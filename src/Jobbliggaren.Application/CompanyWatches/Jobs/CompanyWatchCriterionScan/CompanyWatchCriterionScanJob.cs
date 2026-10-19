using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Jobs.CompanyWatchScan;
using Jobbliggaren.Application.CompanyWatches.Queries;
using Jobbliggaren.Application.Matching.Jobs.BackgroundMatching;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Application.CompanyWatches.Jobs.CompanyWatchCriterionScan;

/// <summary>
/// ADR 0175 (#1663) — the nightly Branschbevakning scan. For every user with a criterion it purges the hits whose
/// ad is no longer Active, then reads each criterion's window through <see cref="ICompanyWatchCriterionAdScan"/>
/// and writes one <see cref="CompanyWatchCriterionAdHit"/> per new Active ad of a member company. Ungraded, under
/// Art. 6(1)(b): no profile is read and nothing is mailed (ADR 0175 D4, D13).
///
/// <para>
/// <b>The window.</b> Its lower bound is <see cref="CompanyWatchCriterion.ScanWindowStart"/> over the cold-start
/// floor; its upper bound is the run clock less <see cref="BackgroundMatchingJob.IngestSettleWindow"/>, and, for
/// an owner who follows a company, never later than that owner's follow watermark (ADR 0175 D3): the follow scan
/// reads with no upper bound, so a followed company's ad then always gets its follow hit first, and the bell keeps
/// it out of this source from its first night. An owner who follows a company but has no follow watermark yet is
/// held. Scanned and TooBroad advance the criterion; NotMaterialised holds it.
/// </para>
///
/// <para>
/// <b>Isolation.</b> Each user runs in its own scope with one <c>SaveChanges</c>, so a failed user is re-scanned
/// cleanly next night; inside a user each criterion's read is isolated, so one criterion that cannot be read
/// does not hold the owner's others (ADR 0175 D6). There is no retry: <c>company_watch_criteria</c> has no
/// concurrency token, so a conflict here means a concurrent delete, which the next night answers.
/// </para>
///
/// <para>
/// <b>Logs carry counts per run</b>, and a user id only on a per-user event: a failed user or a failed criterion
/// read. Never a criterion id, label or code, an ad id or an organisation number (DPIA §D9 M-D10).
/// </para>
/// </summary>
public sealed partial class CompanyWatchCriterionScanJob(
    IServiceScopeFactory scopeFactory,
    IDateTimeProvider clock,
    ILogger<CompanyWatchCriterionScanJob> logger)
{
    private const int ProgressLogEvery = 50;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var settledThrough = now - BackgroundMatchingJob.IngestSettleWindow;
        var coldStartFloor = now.AddDays(-CompanyWatchScanJob.ColdStartDays);

        // Every user with a criterion and a live account: the JobSeeker query filter leaves out an account under
        // deletion, so it gets neither a purge nor a hit (DPIA §D9 M-D8). No per-user Hangfire job (ADR 0161 V7).
        List<Guid> dueUserIds;
        await using (var dueSetScope = scopeFactory.CreateAsyncScope())
        {
            var db = dueSetScope.ServiceProvider.GetRequiredService<IAppDbContext>();
            dueUserIds = await db.CompanyWatchCriteria
                .Where(c => db.JobSeekers.Any(js => js.UserId == c.UserId))
                .Select(c => c.UserId)
                .Distinct()
                .OrderBy(id => id)
                .ToListAsync(cancellationToken);
        }

        LogDueUsers(logger, dueUserIds.Count);

        var tally = new RunTally();
        foreach (var userId in dueUserIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var userScope = scopeFactory.CreateAsyncScope();
                var unit = await ScanUserAsync(
                    userScope.ServiceProvider.GetRequiredService<IAppDbContext>(),
                    userScope.ServiceProvider.GetRequiredService<ICompanyWatchCriterionAdScan>(),
                    userId, settledThrough, coldStartFloor, cancellationToken);
                tally.Add(unit);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One user's failure does not abort the batch. Nothing of the unit is committed but its purge, so
                // the user is scanned again from the same watermarks next night.
                tally.FailedUsers++;
                LogUserFailed(logger, ex, userId);
            }

            tally.Processed++;
            if (tally.Processed % ProgressLogEvery == 0)
                LogProgress(logger, tally.Processed, dueUserIds.Count);
        }

        // AGENTS.md §3.6: once per completed run, fail-loud (the wrapper allows no retry, so a failure is a
        // visible Failed state and the next night runs it again).
        await using (var statisticsScope = scopeFactory.CreateAsyncScope())
        {
            await statisticsScope.ServiceProvider.GetRequiredService<ICompanyWatchCriterionAdHitStatistics>()
                .AnalyzeAsync(cancellationToken);
        }

        LogComplete(logger, tally.Processed, tally.FailedUsers, tally.NoLiveAccount, tally.HeldBehindFollow,
            tally.Scanned, tally.TooBroad, tally.NotMaterialised, tally.EmptyWindow, tally.FailedCriteria,
            tally.Inserted, tally.Purged);
    }

    private async Task<UserTally> ScanUserAsync(
        IAppDbContext db,
        ICompanyWatchCriterionAdScan scan,
        Guid userId,
        DateTimeOffset settledThrough,
        DateTimeOffset coldStartFloor,
        CancellationToken ct)
    {
        var unit = new UserTally();

        // First, whatever follows: the purge holds the stock to Active ads for every due user, a held or refused
        // one included (DPIA §D9 M-D9). Its own statement, idempotent, and it writes no bell flag.
        unit.Purged = await NewCriterionAdSet.HitsWhoseAdLeftActive(db, userId).ExecuteDeleteAsync(ct);

        var seeker = await db.JobSeekers
            .AsNoTracking()
            .Where(js => js.UserId == userId)
            .Select(js => new { js.LastCompanyWatchScanAt })
            .FirstOrDefaultAsync(ct);
        if (seeker is null)
        {
            unit.NoLiveAccount = true;
            return unit;
        }

        var hasActiveFollow = await db.CompanyWatches.AnyAsync(w => w.UserId == userId, ct);
        if (hasActiveFollow && seeker.LastCompanyWatchScanAt is null)
        {
            unit.HeldBehindFollow = true;
            return unit;
        }

        var until = hasActiveFollow && seeker.LastCompanyWatchScanAt < settledThrough
            ? seeker.LastCompanyWatchScanAt.Value
            : settledThrough;

        var criteria = await db.CompanyWatchCriteria
            .Where(c => c.UserId == userId)
            .ToListAsync(ct);

        HashSet<(CompanyWatchCriterionId, JobAdId)>? existing = null;
        foreach (var criterion in criteria)
        {
            var since = criterion.ScanWindowStart(coldStartFloor);
            if (since >= until)
            {
                // A window that cannot hold an ad yet (an edit after the follow run, a stale follow watermark):
                // nothing is read and nothing is recorded, so the last real answer stands.
                unit.EmptyWindow++;
                continue;
            }

            MaterialisedNewAdIds answer;
            try
            {
                answer = await scan.ScanNewActiveAdIdsAsync(
                    criterion.Id, CriteriaFingerprint.Of(criterion.Criteria), since, until, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A read, with nothing of this criterion tracked yet: hold it, keep the owner's others.
                unit.FailedCriteria++;
                continue;
            }

            switch (answer.State)
            {
                case CriterionMaterialisationState.Materialised:
                    if (answer.Ids is { Count: > 0 } ids)
                    {
                        existing ??= await LoadExistingHitKeysAsync(db, userId, ct);
                        foreach (var jobAdId in ids)
                        {
                            if (!existing.Add((criterion.Id, jobAdId)))
                                continue;

                            db.CompanyWatchCriterionAdHits.Add(
                                CompanyWatchCriterionAdHit.Create(criterion, jobAdId, clock).Value);
                            unit.Inserted++;
                        }
                    }

                    criterion.RecordScan(CompanyWatchCriterionScanOutcome.Scanned, until, clock);
                    unit.Scanned++;
                    break;
                case CriterionMaterialisationState.TooBroad:
                    criterion.RecordScan(CompanyWatchCriterionScanOutcome.TooBroad, until, clock);
                    unit.TooBroad++;
                    break;
                case CriterionMaterialisationState.NotMaterialised:
                    criterion.RecordScan(CompanyWatchCriterionScanOutcome.NotMaterialised, until, clock);
                    unit.NotMaterialised++;
                    break;
                default:
                    throw new InvalidOperationException($"Unknown materialisation state {answer.State}.");
            }
        }

        if (unit.FailedCriteria > 0)
            LogCriteriaFailed(logger, userId, unit.FailedCriteria);

        // The hits and the criteria's scan state commit together: a lost advance only re-reads, and the UNIQUE
        // key on (criterion, ad) makes that re-read idempotent.
        await db.SaveChangesAsync(ct);
        return unit;
    }

    // The keys the user's hits already hold, so a re-read inserts nothing twice (the UNIQUE key is the backstop).
    // Loaded once per user and only when some criterion returned ids; compared in memory, because a
    // strongly-typed id inside Contains does not translate.
    private static async Task<HashSet<(CompanyWatchCriterionId, JobAdId)>> LoadExistingHitKeysAsync(
        IAppDbContext db, Guid userId, CancellationToken ct)
    {
        var keys = await db.CompanyWatchCriterionAdHits
            .AsNoTracking()
            .Where(h => h.UserId == userId)
            .Select(h => new { h.CompanyWatchCriterionId, h.JobAdId })
            .ToListAsync(ct);
        return keys.Select(k => (k.CompanyWatchCriterionId, k.JobAdId)).ToHashSet();
    }

    private sealed class UserTally
    {
        public bool NoLiveAccount { get; set; }
        public bool HeldBehindFollow { get; set; }
        public int Scanned { get; set; }
        public int TooBroad { get; set; }
        public int NotMaterialised { get; set; }
        public int EmptyWindow { get; set; }
        public int FailedCriteria { get; set; }
        public int Inserted { get; set; }
        public int Purged { get; set; }
    }

    private sealed class RunTally
    {
        public int Processed { get; set; }
        public int FailedUsers { get; set; }
        public int NoLiveAccount { get; private set; }
        public int HeldBehindFollow { get; private set; }
        public int Scanned { get; private set; }
        public int TooBroad { get; private set; }
        public int NotMaterialised { get; private set; }
        public int EmptyWindow { get; private set; }
        public int FailedCriteria { get; private set; }
        public int Inserted { get; private set; }
        public int Purged { get; private set; }

        public void Add(UserTally unit)
        {
            NoLiveAccount += unit.NoLiveAccount ? 1 : 0;
            HeldBehindFollow += unit.HeldBehindFollow ? 1 : 0;
            Scanned += unit.Scanned;
            TooBroad += unit.TooBroad;
            NotMaterialised += unit.NotMaterialised;
            EmptyWindow += unit.EmptyWindow;
            FailedCriteria += unit.FailedCriteria;
            Inserted += unit.Inserted;
            Purged += unit.Purged;
        }
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "CompanyWatchCriterionScanJob: {Count} users with a Branschbevakning to scan")]
    private static partial void LogDueUsers(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "CompanyWatchCriterionScanJob: {Processed}/{Total} users scanned")]
    private static partial void LogProgress(ILogger logger, int processed, int total);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "CompanyWatchCriterionScanJob: scan failed for user {UserId} — isolated, scanned again next run")]
    private static partial void LogUserFailed(ILogger logger, Exception ex, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "CompanyWatchCriterionScanJob: {FailedCriteria} Branschbevakningar of user {UserId} could not be read — held, read again next run")]
    private static partial void LogCriteriaFailed(ILogger logger, Guid userId, int failedCriteria);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "CompanyWatchCriterionScanJob: done — {Processed} users ({FailedUsers} failed, {NoLiveAccount} without a live account, {HeldBehindFollow} held behind the follow scan); criteria {Scanned} scanned, {TooBroad} too broad, {NotMaterialised} not materialised, {EmptyWindow} with an empty window, {FailedCriteria} failed; {Inserted} hits written, {Purged} hits purged")]
    private static partial void LogComplete(
        ILogger logger, int processed, int failedUsers, int noLiveAccount, int heldBehindFollow, int scanned,
        int tooBroad, int notMaterialised, int emptyWindow, int failedCriteria, int inserted, int purged);
}
