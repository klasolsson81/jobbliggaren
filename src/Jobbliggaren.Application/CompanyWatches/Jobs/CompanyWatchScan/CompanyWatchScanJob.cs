using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Application.CompanyWatches.Jobs.CompanyWatchScan;

/// <summary>
/// ADR 0087 D5 (#311 PR-4) + 7C (bevakning-reconcile RF-7, 2026-07-12) — the scheduled
/// company-follow scan. For every user with at least one ACTIVE follow, it finds the ads published
/// since that user's <see cref="Domain.JobSeekers.JobSeeker.LastCompanyWatchScanAt"/> watermark
/// whose <c>organization_number</c> is one of the user's ACTIVE watched org.nrs, and persists a
/// <see cref="FollowedCompanyAdHit"/> for each. Registered as a Hangfire RecurringJob in the same
/// nightly window as <c>BackgroundMatchingJob</c>, with its OWN watermark. NO AI/LLM, NO skill
/// scorer (a company hit requires only org.nr membership — ADR 0087 D5).
/// <para>
/// <b>7C consent split (bevakning-reconcile RF-7, 2026-07-12 — Klas-ratified; supersedes ADR 0087
/// D5's scan-time consent gate):</b> creating a hit is part of the SERVICE a user requested by
/// following the company (GDPR Art. 6(1)(b)), so the scan persists hits for EVERY active follower
/// with NO consent predicate. The SEPARATE email opt-in
/// (<c>FollowedCompanyNotificationsEnabled</c>, Art. 6(1)(a)) gates only the
/// <c>DigestDispatchJob</c> email pass at DISPATCH time — never hit creation here.
/// </para>
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
    IProtectedIdentityTokenizer tokenizer,
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

        // 7C (bevakning-reconcile RF-7, 2026-07-12) — the due set is EVERY user with at least one
        // ACTIVE follow, NOT a consent-gated subset. Following a company IS the request for the
        // in-app notification service (GDPR Art. 6(1)(b)); the email channel stays consent-gated
        // (Art. 6(1)(a)) at DISPATCH, never here. Supersedes ADR 0087 D5's scan-time consent gate
        // (explicit supersession #2, Klas-ratified). The CompanyWatches query filter excludes
        // soft-deleted/unfollowed rows, so DISTINCT UserId over it IS the active-follower set.
        var followerUserIds = await db.CompanyWatches
            .Select(w => w.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        LogFollowers(logger, followerUserIds.Count);

        var processedUsers = 0;
        var totalHits = 0;
        foreach (var userId in followerUserIds)
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
                LogProgress(logger, processedUsers, followerUserIds.Count);
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
            return 0; // a follower UserId without a JobSeeker (shouldn't happen) — nothing to scan.

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

        // #544 (ADR 0090 D5 / CTO D1-D2) — partition the active watches by at-rest form. A
        // legal-entity (AB) org.nr is stored plaintext and matched by the unchanged SQL IN (the
        // majority path). A personnummer-shaped (enskild-firma) org.nr is stored as an HMAC token
        // that cannot match the plaintext job_ads column in SQL, so its ads are found by an in-memory
        // HMAC match over the pnr-shaped candidate subset (mechanism 1, hybrid pnr-only).
        // IsPersonnummerShaped is the SSOT discriminator (B2): a stored token → true (length≠10), an
        // AB plaintext → false. Whole-watch values (RF-2/RF-3) so the per-watch Filter is in the loop.
        var abWatchByOrgNr = new Dictionary<string, CompanyWatch>(StringComparer.Ordinal);
        var enskildWatchByKey = new Dictionary<string, CompanyWatch>(StringComparer.Ordinal);
        foreach (var w in activeWatches)
        {
            // BRAND_GROUP watches (null org.nr) are not org.nr-keyed — group expansion lands in PR-5
            // Commit 3. Until then a group watch cannot exist at runtime (no write path yet); this
            // guard preserves employer-only behaviour and keeps the partition null-safe.
            if (w.OrganizationNumber is null)
                continue;
            if (w.OrganizationNumber.IsPersonnummerShaped())
                enskildWatchByKey[w.OrganizationNumber.Value] = w; // key = token (or legacy raw pnr)
            else
                abWatchByOrgNr[w.OrganizationNumber.Value] = w; // key = plaintext AB org.nr
        }
        // IReadOnlyList<string> (not List<string>) so the org.nr membership uses the LINQ
        // Enumerable.Contains overload — EF translates it to SQL IN over the nullable shadow column
        // (parity the D6 ApplyFilter employer filter; a List<string>.Contains would reject the
        // string? column arg at compile time).
        IReadOnlyList<string> abOrgNrs = abWatchByOrgNr.Keys.ToList();

        var since = jobSeeker.LastCompanyWatchScanAt ?? now.AddDays(-ColdStartDays);

        // Filter by CreatedAt (INGEST time), not PublishedAt (parity BackgroundMatchingJob). ONE
        // round-trip loads every new active ad whose org.nr is EITHER a watched AB org.nr (SQL IN,
        // unchanged) OR pnr-shaped (a possible enskild match, resolved in memory). The pnr-shape arm
        // (Length==10 AND 3rd digit 0/1) is a translatable SUPERSET of every ad that could HMAC-match
        // an enskild watch — a matching ad carries the watch's own valid 10-digit pnr — so a too-narrow
        // prefilter (the cardinal sin: a watch that matches nothing) fails the Testcontainers oracle,
        // never silently in prod. The IDENTICAL predicate lives in ListCompanyWatchesQueryHandler's
        // token→plaintext resolution — kept in sync BY HAND, deliberately NOT single-sourced: 2 call
        // sites is below the §3.6 rule-of-three and single-sourcing this OR-disjunct would force an
        // OrElse predicate combinator the repo won't take (LinqKit is off the BUILD.md §3.1 allowlist,
        // and hand-rolling its ExpressionVisitor dodges that discipline) — declined, dotnet-architect +
        // senior-cto-advisor 2026-07-18; each copy oracle-pinned independently: this Scan arm by
        // RunAsync_PnrShapePrefilter_AdmitsBothBoundaryThirdDigits_TheSupersetPin, the List arm by
        // CompanyWatchesTests.GET_list_reports_active_ad_count_even_when_org_number_is_masked). Project the id + its org.nr (mapped back to the watch client-side, no
        // join) + BOTH geo axes (per-watch ort filter, RF-3=3D — the ort check is CLIENT-SIDE per
        // (ad, watch) pair; the D5 seal EXTENDED, still scorer-/profile-free). Both geo axes are needed:
        // an ad tagged at län granularity with NO municipality must still pass a whole-län filter (F4a).
        var newAds = await db.JobAds
            .AsNoTracking()
            .Where(j => j.Status == JobAdStatus.Active
                        && j.CreatedAt > since
                        && (abOrgNrs.Contains(EF.Property<string?>(j, "OrganizationNumber"))
                            || (EF.Property<string?>(j, "OrganizationNumber") != null
                                && EF.Property<string?>(j, "OrganizationNumber")!.Length == 10
                                && (EF.Property<string?>(j, "OrganizationNumber")!.Substring(2, 1) == "0"
                                    || EF.Property<string?>(j, "OrganizationNumber")!.Substring(2, 1) == "1"))))
            .Select(j => new
            {
                j.Id,
                OrgNr = EF.Property<string?>(j, "OrganizationNumber"),
                Municipality = EF.Property<string?>(j, "MunicipalityConceptId"),
                Region = EF.Property<string?>(j, "RegionConceptId"),
                // #551 PR-B D6 — the ad's remote flag (PR-A bool column) feeds AdmitsLocation's
                // remote disjunct so a per-watch remote filter admits a remote (location-less) ad.
                j.Remote,
            })
            .ToListAsync(ct);

        var hitCount = 0;
        if (newAds.Count > 0)
        {
            // Idempotency backstop: UNIQUE(UserId, JobAdId, CompanyWatchId) prevents re-notification,
            // but skip already-persisted triples so the insert batch never throws on a window overlap
            // / re-run. Load the user's existing (jobAdId, watchId) pairs client-side (bounded by
            // their accumulated hits) — avoids the strongly-typed-VO Contains translation trap.
            // No IgnoreQueryFilters: the aggregate has no soft-delete filter (#868 retired the
            // writerless axis), so there is nothing to bypass.
            var existing = (await db.FollowedCompanyAdHits
                    .Where(h => h.UserId == userId)
                    .Select(h => new { h.JobAdId, h.CompanyWatchId })
                    .ToListAsync(ct))
                .Select(x => (x.JobAdId, x.CompanyWatchId))
                .ToHashSet();

            foreach (var ad in newAds)
            {
                if (ad.OrgNr is null)
                    continue;

                // Resolve the originating watch. AB org.nrs match plaintext directly; a pnr-shaped ad
                // HMAC-matches an enskild watch token (or, during the backfill window, equals a legacy
                // plaintext-pnr watch — the same dual-probe as the follow seam). An ad admitted only by
                // the pnr-shape prefilter that matches no enskild watch falls through (most pnr-shaped
                // ads have no follower). The tokenizer runs only when the user actually holds an enskild
                // follow.
                CompanyWatch? watch = null;
                if (abWatchByOrgNr.TryGetValue(ad.OrgNr, out var abWatch))
                {
                    watch = abWatch;
                }
                else if (enskildWatchByKey.Count > 0)
                {
                    if (enskildWatchByKey.TryGetValue(tokenizer.Tokenize(ad.OrgNr), out var tokenWatch))
                        watch = tokenWatch;
                    else if (enskildWatchByKey.TryGetValue(ad.OrgNr, out var legacyWatch))
                        watch = legacyWatch;
                }

                if (watch is null)
                    continue;

                // Per-watch ort filter (RF-3=3D scan-time / RF-8=8A never-created): an active ort
                // filter admits an ad whose municipality OR whose region is selected — a filtered-out
                // ad produces NO hit row (data minimization). An ad tagged with NEITHER axis never
                // passes an active ort filter (the VO's AdmitsLocation semantics). No filter → all pass.
                if (watch.Filter is { } filter
                    && !filter.AdmitsLocation(ad.Municipality, ad.Region, ad.Remote))
                {
                    continue;
                }

                if (existing.Contains((ad.Id, watch.Id)))
                    continue;

                var created = FollowedCompanyAdHit.Create(userId, ad.Id, watch.Id, clock);
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
        Message = "CompanyWatchScanJob: {Count} users with active follows to scan")]
    private static partial void LogFollowers(ILogger logger, int count);

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
