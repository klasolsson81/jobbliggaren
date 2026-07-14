using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Notifications;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Matching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Application.Matching.Jobs.DigestDispatch;

/// <summary>
/// ADR 0080 Vag 4 PR-4b (Beslut 4) — the Strong-match digest dispatch. For every CONSENTING user
/// whose chosen <see cref="DigestCadence"/> matches the run (the cron IS the window: a Daily cron
/// dispatches Daily users, a Weekly cron the Weekly users), it composes ONE email summarising the
/// user's accumulated Pending <see cref="NotifiableMatchGrade.Strong"/> matches and marks them
/// Sent. <see cref="NotifiableMatchGrade.Good"/> is in-app only (never emailed) and
/// <see cref="NotifiableMatchGrade.Top"/> is direct-dispatched in the scan — so neither is digested
/// here. Registered as two Hangfire RecurringJobs (~06:00 UTC, deliberately AFTER the 03:20 scan).
/// NO AI/LLM (ADR 0071).
/// <para>
/// <b>Consent is the query gate (GDPR Art. 6/7) for the STRONG-match pass:</b> opt-in ON and not
/// withdrawn — identical to the background-match scan (NOT the company-follow scan, which under 7C
/// creates hits for every active follower — see the follow-pass section below). A withdrawal stops
/// dispatch immediately (its Pending rows are simply never picked up).
/// </para>
/// <para>
/// <b>Idempotent claim-then-send:</b> HTTP is not transactional, so per user the sequence is
/// claim (<c>Pending → MarkQueued</c> + commit) → send → drain (<c>Queued → MarkSent</c> + commit).
/// The claim before the send is the idempotency spine — a Hangfire retry or a re-run never
/// re-sends (the digest filters Strong + Pending; a claimed row is Queued). A send failure after
/// the claim strands the rows in Queued (never re-sent): the deliberate "never double-email &gt;
/// never miss" MVP trade-off (the stranded-Queued reaper is TD-114).
/// </para>
/// <para>
/// <b>Anti-spam cap (ADR 0080 Negativa):</b> the body LISTS at most
/// <see cref="DigestDispatchOptions.MaxItemsPerDigest"/> items while <c>TotalCount</c> reports the
/// PRESENTABLE window — the rows whose ad is <c>Active</c> (#864). It is NOT "the full window": that
/// phrasing meant the CLAIM set, and wiring the count to it made the email render "och N till" about
/// ads the body can never list and the recipient can never open.
/// </para>
/// <para>
/// <b>The row's two ends, and the predicate that governs both (#864).</b> A pending row is partitioned
/// by ONE question — <i>is this ad still presentable?</i>
/// <list type="bullet">
/// <item><b>claim</b> = presentable ∧ grade passes → claimed, emailed, drained (Sent).</item>
/// <item><b>stayPending</b> = presentable ∧ grade fails → left UNTOUCHED. Grade is a function of
/// (profile, ad) and the profile is MUTABLE, so the hit may legitimately re-surface later (8C).</item>
/// <item><b>drain</b> = NOT presentable → drained unconditionally, never shown. Lifecycle is a function
/// of the AD ALONE and is MONOTONE: nothing un-archives an ad, so waiting is not deferral — it is a
/// leak, and nothing reaps it (the stranded reaper takes QUEUED rows; a row never claimed is never
/// queued).</item>
/// </list>
/// <b>Lifecycle is a DRAIN reason; grade is a FILTER-OUT reason. Never the other way round.</b>
/// </para>
/// <para>
/// <b>Per-user failure is isolated</b> (one user's exception does not abort the run — TD-25),
/// parity <see cref="BackgroundMatching.BackgroundMatchingJob"/>.
/// </para>
/// <para>
/// <b>ADR 0087 D5 (#311 PR-4) — company-follow digest pass.</b> This job ALSO dispatches
/// <c>FollowedCompanyAdHit</c> rows (new ads from employers a user follows) via the SEPARATE
/// <see cref="IEmailSender.SendFollowedCompanyNotificationEmailAsync"/> contract, gated by the
/// SEPARATE <c>FollowedCompanyNotificationsEnabled</c> consent flag on the SHARED cadence. The
/// follow pass is a fully INDEPENDENT second pass (its own due-set query, per-user loop, and failure
/// isolation) — fetching/dispatching follow hits never shares state with the Strong-match pass (the
/// SoC ADR 0087 D5 mandates). A user consenting to both at this cadence honestly gets two emails.
/// </para>
/// </summary>
public sealed partial class DigestDispatchJob(
    IAppDbContext db,
    IEmailSender emailSender,
    IUserAccountService userAccounts,
    IMatchProfileBuilder profileBuilder,
    IPerUserJobAdSearchQuery perUserSearch,
    IDateTimeProvider clock,
    IOptions<DigestDispatchOptions> options,
    ILogger<DigestDispatchJob> logger)
{
    private readonly DigestDispatchOptions _options = options.Value;

    // The grade filter is inert (no OnlyMatched watch, or a profile-less user) → no ad is "matching",
    // and GradePasses short-circuits on gradeFilterActive before ever reading this.
    private static readonly IReadOnlySet<JobAdId> EmptyJobAdIds = new HashSet<JobAdId>();

    public async Task RunAsync(DigestCadence cadence, CancellationToken cancellationToken)
    {
        // The DUE set: consenting users whose chosen cadence is the one this run dispatches (the
        // cron IS the window). Consent filter parity the scan — enabled AND not withdrawn. Default
        // OFF → the set is small; a per-user loop is fine for the $16-VPS MVP.
        var dueUserIds = await db.JobSeekers
            .Where(js => js.Preferences.BackgroundMatchNotificationsEnabled
                         && js.Preferences.NotificationConsentWithdrawnAt == null
                         && js.Preferences.DigestCadence == cadence)
            .Select(js => js.UserId)
            .ToListAsync(cancellationToken);

        LogDue(logger, cadence, dueUserIds.Count);

        var processed = 0;
        var sent = 0;
        foreach (var userId in dueUserIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await DispatchUserDigestAsync(userId, cadence, cancellationToken))
                    sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Per-user isolation (TD-25): one user's failure must not abort the run. A
                // cancellation propagates (host shutdown / cron-timeout) — not mis-logged as a
                // user failure.
                LogUserFailed(logger, ex, userId);
            }

            processed++;
        }

        LogComplete(logger, cadence, processed, sent);

        // ─── Company-follow digest pass (ADR 0087 D5) — INDEPENDENT of the Strong-match pass above.
        // A SEPARATE consent flag (FollowedCompanyNotificationsEnabled) gates a SEPARATE source
        // aggregate (FollowedCompanyAdHit) sent via a SEPARATE email contract. Its own due-set query
        // + per-user loop + failure isolation → the two sources never share fetch/dispatch state (the
        // SoC ADR 0087 D5 mandates). The digest CADENCE is shared (ADR 0087 D2), so a user consenting
        // to BOTH at this cadence honestly gets two emails (two distinct GDPR purposes).
        await DispatchFollowedCompanyDigestsAsync(cadence, cancellationToken);
    }

    private async Task DispatchFollowedCompanyDigestsAsync(
        DigestCadence cadence, CancellationToken cancellationToken)
    {
        var dueUserIds = await db.JobSeekers
            .Where(js => js.Preferences.FollowedCompanyNotificationsEnabled
                         && js.Preferences.FollowedCompanyNotificationConsentWithdrawnAt == null
                         && js.Preferences.DigestCadence == cadence)
            .Select(js => js.UserId)
            .ToListAsync(cancellationToken);

        LogFollowDue(logger, cadence, dueUserIds.Count);

        var processed = 0;
        var sent = 0;
        foreach (var userId in dueUserIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await DispatchUserFollowedCompanyDigestAsync(userId, cadence, cancellationToken))
                    sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Per-user isolation (TD-25), parity the match pass.
                LogFollowUserFailed(logger, ex, userId);
            }

            processed++;
        }

        LogFollowComplete(logger, cadence, processed, sent);
    }

    private async Task<bool> DispatchUserDigestAsync(
        Guid userId, DigestCadence cadence, CancellationToken ct)
    {
        // The user's Pending STRONG rows (tracked — MarkQueued/MarkSent mutate them). Order by
        // recency (Goodhart: grade + CreatedAt, NEVER a score). Good is in-app only; Top is
        // direct-dispatched in the scan — the Grade filter excludes both.
        var pending = await db.UserJobAdMatches
            .Where(m => m.UserId == userId
                        && m.Grade == NotifiableMatchGrade.Strong
                        && m.NotificationStatus == NotificationStatus.Pending)
            .OrderByDescending(m => m.CreatedAt)
            .ThenBy(m => m.Id)
            .ToListAsync(ct);

        if (pending.Count == 0)
            return false;

        // Display items (capped) for the SAME Pending Strong rows, joined to each ad's PUBLIC
        // title/company (no CV data). Read BEFORE the claim (rows still Pending), AsNoTracking,
        // same ordering → the most recent N of the PRESENTABLE window.
        //
        // LIFECYCLE (#864) — `j.Status == JobAdStatus.Active` is an EXPLICIT predicate here. JobAd
        // carries no query filter (#821 retired the dead soft-delete axis), so an ARCHIVED ad still
        // JOINS; the old comment claimed the join dropped "an ad that is gone" and called the absence
        // of a Status predicate "deliberate parity with /matchningar". Both were false: an archived ad
        // is not gone, archiving is every ad's normal end of life (ExpireJobAdsJob), and the parity was
        // parity with a defect. BackgroundMatchingJob's gate only proves the ad was Active AT SCAN
        // TIME — weeks before this digest runs. Un-gated, this job EMAILED "Stark match" for ads
        // nobody can apply to. The parity with /matchningar survives, but it is now a GATED parity
        // (GetMyMatchesQueryHandler carries the same predicate). ALLOW-list, not `!= Archived`: a
        // deny-list admits every status added later, and Erased (#842) is a tombstone whose company
        // reads "[raderad]" — pushing that into an inbox would broadcast the very erasure it exists
        // to conceal.
        //
        // The gate is on the DISPLAY set only. `pending` above is the CLAIM/DRAIN set and stays
        // UNGATED ON PURPOSE: an archived row was a valid match when detected, so it must still be
        // drained (Pending → Queued → Sent) or it re-processes on every digest run, forever.
        // Joining (not filtering by an id set) sidesteps the strongly-typed-VO Contains trap.
        var presentable =
            from m in db.UserJobAdMatches.AsNoTracking()
            where m.UserId == userId
                  && m.Grade == NotifiableMatchGrade.Strong
                  && m.NotificationStatus == NotificationStatus.Pending
            join j in db.JobAds.AsNoTracking() on m.JobAdId equals j.Id
            where j.Status == JobAdStatus.Active
            orderby m.CreatedAt descending, m.Id
            select new { j.Title, Company = j.Company.Name };

        // ONE materialisation, then count, THEN cap. The count and the displayed rows come from the SAME
        // list, so `TotalCount >= Items.Count` holds BY CONSTRUCTION.
        //
        // The obvious shape — `.Take(cap)` for the body plus a separate `.CountAsync()` for the total —
        // is wrong here, and not because of the round-trip. It is TWO statements with no enclosing
        // transaction, and ExpireJobAdsJob is not serialised against this job: archive an ad between them
        // and the total comes back SMALLER than the list, so the email says "har hittat 1 nya matchningar
        // sedan sist:" above a body that lists two. That degrades a structural invariant into a TOCTOU
        // race — trading the lie this PR removes for a rarer one. Materialising once cannot race with
        // itself. (An invariant you assert is a test; an invariant you construct is a guarantee.)
        //
        // Unbounded is not a new risk: `pending` above already loads the same rows with no Take, as
        // TRACKED entities. This is a strictly cheaper AsNoTracking projection of a subset of them.
        var presentableRows = await presentable.ToListAsync(ct);

        // The "och N till" remainder renders as TotalCount - Items.Count, so TotalCount must count what
        // the user COULD have been shown — the PRESENTABLE window, never `pending` (the claim set), which
        // would promise archived ads the body can never list. A count that promises more than its set can
        // deliver is the #560 PR-2 defect, and it would have arrived here inside the fix for #864 — in an
        // outbound email, under a field the DTO's own docstring calls the honest total.
        var presentableTotal = presentableRows.Count;
        var displayRows = presentableRows
            .Take(_options.MaxItemsPerDigest)
            .ToList();

        // Claim ALL pending Strong rows (Pending → Queued) and commit BEFORE the send — the
        // idempotency spine. MarkQueued's Result is structurally Success (the rows were loaded
        // WHERE Pending; nothing mutates them between the load and here — single-threaded,
        // DisableConcurrentExecution).
        foreach (var match in pending)
            match.MarkQueued();
        await db.SaveChangesAsync(ct);

        var toEmail = await userAccounts.GetEmailAsync(userId, ct);
        if (string.IsNullOrWhiteSpace(toEmail))
        {
            // Orphan consent row without an account email — the claimed rows stay Queued (TD-114).
            LogNoEmail(logger, userId);
            return false;
        }

        if (displayRows.Count == 0)
        {
            // Nothing PRESENTABLE — every matched ad's row is gone, or (since #864) every one of them
            // is archived. Drain the whole claimed window (mark Sent) so it doesn't re-process every
            // run; send nothing (an empty digest is noise, not a notification).
            DrainSent(pending);
            await db.SaveChangesAsync(ct);
            LogEmptyDrained(logger, pending.Count, userId);
            return false;
        }

        // Strong is the only grade in this batch (the query filters it) → "Stark match" for every
        // item. TotalCount is the PRESENTABLE window (#864), not `pending.Count`: the body lists the
        // cap and renders "och N till" for the remainder, so the total must count only ads the body
        // could have listed.
        var items = displayRows
            .Select(r => new MatchNotificationItem(
                r.Title, r.Company, NotifiableMatchGrade.Strong.ToSwedishLabel()))
            .ToList();
        var content = new MatchNotificationEmail(
            MatchNotificationKind.Digest, cadence, items, presentableTotal);

        // Idempotency key (#187): key the CONTENT of the claimed Strong set (a content hash of the
        // claimed match ids), NOT a wall-clock window — so two same-period runs that claimed
        // different sets get different keys and Resend never sees a key/payload mismatch (409). The
        // factory sorts the ids itself; stable across a transport-retry within this single dispatch.
        var idempotencyKey = MatchNotificationIdempotencyKey.ForDigest(
            userId, cadence, pending.Select(m => m.Id.Value));

        try
        {
            await emailSender.SendMatchNotificationEmailAsync(toEmail, content, idempotencyKey, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Send failed AFTER the claim → rows stay Queued, never re-sent ("never double-email >
            // never miss"; TD-114 reaper). No rethrow — the matches persist; only this run's email
            // is missed (any NEW Strong match still accumulates for the next digest).
            LogSendFailed(logger, ex, userId);
            return false;
        }

        // Drain: mark ALL window rows Sent (not just the displayed cap) so the un-displayed
        // remainder cannot re-surface next digest.
        DrainSent(pending);
        await db.SaveChangesAsync(ct);
        LogSent(logger, userId, presentableTotal, items.Count, pending.Count);
        return true;
    }

    // Queued → Sent for the whole claimed batch. MarkSent's Result is structurally Success (the
    // rows were just claimed Queued).
    private void DrainSent(IReadOnlyList<UserJobAdMatch> matches)
    {
        foreach (var match in matches)
            match.MarkSent(clock);
    }

    // ─── Company-follow digest (ADR 0087 D5) — the SHAPE of DispatchUserDigestAsync above, but over
    // FollowedCompanyAdHit rows + the FollowedCompanyNotificationEmail contract. Kept as a SEPARATE
    // method so the two aggregate sources never share fetch/dispatch state (SoC). The hit + contract
    // stay grade-FREE (Goodhart/D1 seal); 7C adds a read-time per-watch OnlyMatched grade FILTER that
    // decides email INCLUSION only — the grade is never persisted or surfaced per item.
    private async Task<bool> DispatchUserFollowedCompanyDigestAsync(
        Guid userId, DigestCadence cadence, CancellationToken ct)
    {
        // The user's Pending follow-hit rows (tracked — MarkQueued/MarkSent mutate them). Ordered by
        // recency (CreatedAt desc, then Id for determinism) — the HIT carries no grade (Goodhart seal).
        // #453 (cross-channel dedup) — AND SeenAt == null: a hit the user already opened in-app is
        // suppressed ("aldrig mejla något jag sett i appen"). A stamped-but-Pending hit is never claimed
        // here (falls dormant) and the scan's triple-dedup never re-creates it.
        var pending = await db.FollowedCompanyAdHits
            .Where(h => h.UserId == userId
                        && h.NotificationStatus == FollowedCompanyAdHitStatus.Pending
                        && h.SeenAt == null)
            .OrderByDescending(h => h.CreatedAt)
            .ThenBy(h => h.Id)
            .ToListAsync(ct);

        if (pending.Count == 0)
            return false;

        // ─── Per-watch "endast matchade" filter (bevakning-reconcile RF-3/RF-5/RF-8=8C, 2026-07-12).
        // Read-time, grade NEVER persisted (Goodhart — the hit has no grade column). The ort filter was
        // already applied SCAN-time (8A, F1); only the OnlyMatched flag needs a dispatch-time grade
        // check. Load the user's ACTIVE watches (whole-watch load, parity the scan/ListCompanyWatches)
        // → CompanyWatchId → WatchFilterSpec?. A hit whose watch was unfollowed since creation is absent
        // → treated as no-filter → passes (its WatchFilterSpec was cleared on SoftDelete anyway).
        var filterByWatchId = (await db.CompanyWatches
                .AsNoTracking()
                .Where(w => w.UserId == userId)
                .ToListAsync(ct))
            .ToDictionary(w => w.Id, w => w.Filter);

        bool NeedsGradeCheck(FollowedCompanyAdHit h) =>
            filterByWatchId.TryGetValue(h.CompanyWatchId, out var f) && f is { OnlyMatched: true };

        // ─── PRESENTABILITY IS ITS OWN READ (#864). Read BEFORE the claim (the join predicate needs the
        // rows still Pending). The PUBLIC title/company per ad — never the org.nr (ADR 0087 D8).
        //
        // `j.Status == JobAdStatus.Active` is an EXPLICIT predicate. The old comment here said this join
        // "drops a hit whose ad row is GONE" and then conceded it "is not a lifecycle filter". Right about
        // the mechanism, wrong about the consequence: an ARCHIVED ad is not gone — it joins, and this job
        // EMAILED it. CompanyWatchScanJob's own Active gate (:156) only proves the ad was live when the
        // hit was RECORDED, weeks before this digest runs, and archiving is every ad's normal end of life.
        // ALLOW-list, never `!= Archived` (see the match digest; an Erased tombstone must never reach an
        // inbox). Joining, not an id-set filter, sidesteps the strongly-typed-VO Contains trap.
        //
        // This read must NOT be replaced by inferring non-presentability from FilterToMatchingAsync's
        // absence. That port conflates TWO predicates — `Status == Active` AND grade ≥ Good
        // (PerUserJobAdSearchQuery:370) — into ONE set, so its result cannot tell you WHY a hit is missing
        // from it. That conflation is exactly what hid the leak described at the partition below.
        var itemsQuery =
            from h in db.FollowedCompanyAdHits.AsNoTracking()
            where h.UserId == userId
                  && h.NotificationStatus == FollowedCompanyAdHitStatus.Pending
                  && h.SeenAt == null
            join j in db.JobAds.AsNoTracking() on h.JobAdId equals j.Id
            where j.Status == JobAdStatus.Active
            orderby h.CreatedAt descending, h.Id
            select new { h.JobAdId, j.Title, Company = j.Company.Name };

        var presentableRows = await itemsQuery.ToListAsync(ct);
        var presentableAdIds = presentableRows.Select(r => r.JobAdId).ToHashSet();

        // Grade only the hits that could actually be SHOWN. Grading a dead ad is wasted work and — far
        // worse — feeding one to the port would let its absence from the result be misread as "below
        // ≥Good", which is the conflation that hid the leak.
        var idsToGrade = pending
            .Where(h => NeedsGradeCheck(h) && presentableAdIds.Contains(h.JobAdId))
            .Select(h => h.JobAdId)
            .Distinct()
            .ToList();

        // Assessability is a property of the USER, not of this email's hit set (CTO sub-bind A′,
        // 2026-07-12). The 13B disclosure must fire for an OnlyMatched watch even when that watch
        // contributed NO pending hits this window — so we cannot infer assessability from
        // `idsToGrade` being non-empty. Gate the profile build on whether the user has ANY active
        // OnlyMatched watch: an in-memory Any() over data already loaded, so the common path (no
        // such watch) still costs nothing and the fail-fast port is never called on an empty-SSYK
        // profile. The profile is built AT MOST ONCE and reused for the grade filter below.
        // ONE carrier for the whole "we may grade this user" invariant: a NON-NULL profile IS the
        // assessability claim. A (bool, nullable-profile) pair would only ASSERT the coupling — and
        // would need a null-forgiving `!` at the call site to say what the compiler cannot see.
        FullCandidateMatchProfile? assessableProfile = null;
        if (filterByWatchId.Values.Any(f => f is { OnlyMatched: true }))
        {
            var profile = await profileBuilder.BuildFullForUserIdAsync(userId, ct);

            // A profile-less user (no stated occupation) makes the filter INERT (RF-5 under-fork i):
            // deliver unfiltered rather than a dishonest empty set, and disclose nothing (claiming an
            // inert filter narrowed something is a §5 accuracy miss). Leaving the carrier null here is
            // what keeps the fail-fast port from ever seeing an empty-SSYK profile.
            if (profile.Fast.SsykGroupConceptIds.Count > 0)
                assessableProfile = profile;
        }

        // ═══ THE PARTITION (#864). `pending` splits into THREE disjoint, exhaustive classes:
        //
        //     claim       =  presentable ∧  gradePasses   → claimed, emailed, drained (Sent)
        //     stayPending =  presentable ∧ ¬gradePasses   → untouched (8C: the profile may change)
        //     drain       = ¬presentable                  → drained unconditionally, never shown
        //
        // `effective` is claim ∪ drain — every row this run consumes.
        //
        // LIFECYCLE IS A DRAIN REASON. GRADE IS A FILTER-OUT REASON. The asymmetry is a domain truth,
        // not a preference:
        //
        //   • GRADE is a function of (profile, ad), and the profile is MUTABLE. So a hit below ≥Good may
        //     legitimately re-surface later — that is precisely what 8C's "leave it Pending" is FOR.
        //   • LIFECYCLE is a function of the AD ALONE, and it is MONOTONE. Nothing un-archives an ad.
        //     Waiting for one is not deferral; it is a leak.
        //
        // Before #864, lifecycle acted as a filter-out reason BY ACCIDENT: an archived hit under an
        // OnlyMatched watch fell out of `FilterToMatchingAsync` — that port carries its own
        // `Status == Active` gate (PerUserJobAdSearchQuery:370) — and so was never claimed, never
        // drained, and stayed Pending FOREVER, re-graded (one round-trip) on every digest run for the
        // life of the row. NOTHING REAPED IT: StrandedMatchReaperJob reaps QUEUED rows, and a row that is
        // never claimed is never queued. The leak was unbounded and monotonically growing, because
        // archiving is every ad's normal end of life.
        IReadOnlySet<JobAdId> matching = EmptyJobAdIds;
        var gradeFilterActive = idsToGrade.Count > 0 && assessableProfile is not null;
        if (gradeFilterActive)
        {
            matching = await perUserSearch.FilterToMatchingAsync(assessableProfile!, idsToGrade, ct);
        }

        // A hit clears the grade floor when no filter applies to it, or the port returned it.
        bool GradePasses(FollowedCompanyAdHit h) =>
            !gradeFilterActive || !NeedsGradeCheck(h) || matching.Contains(h.JobAdId);

        var effective = pending
            .Where(h => !presentableAdIds.Contains(h.JobAdId) || GradePasses(h))
            .ToList();

        if (effective.Count == 0)
        {
            // Every pending hit is PRESENTABLE and graded out — nothing to email, and nothing that may be
            // consumed. Leave them Pending for retroactive re-surfacing (8C); the deferred retention
            // sweep (DPIA R-E2/M-E2) bounds the accumulation. A NON-presentable hit can never reach this
            // return — it is in `effective` by construction — so this early exit can no longer strand a
            // dead row. It did, before #864.
            return false;
        }

        // 13B filter-summary (CTO sub-bind A′) — a standing caveat about the user's SETTINGS, not a
        // report of what this particular email dropped. It therefore quantifies over ALL the user's
        // active watch filters, exactly as the rendered Swedish sentence does ("ett eller flera av
        // företagen du följer"). Quantifying over the CONTRIBUTING watches instead would make the
        // disclosure's ABSENCE a false claim: a watch whose filter suppressed 100% of that company's
        // new ads contributes zero hits, so the email would stay silent about a real narrowing —
        // the very failure RF-13 rejected, reached by another route. Email-level, grade-free (D1 seal).
        var filterSummary = BuildFilterSummary(
            filterByWatchId, onlyMatchedAssessable: assessableProfile is not null);

        // The BODY is the CLAIM class: presentable ∧ gradePasses. `presentableRows` was read above (in
        // display order); intersecting it with `effective` removes the drain class — which is
        // ¬presentable and therefore already absent from it — so what survives is exactly claim.
        //
        // ONE materialisation, then count, THEN cap: `TotalCount >= Items.Count` holds BY CONSTRUCTION.
        // A `.Take(cap)` for the body plus a separate `.CountAsync()` for the total would be two
        // unsynchronised statements with no transaction, and ExpireJobAdsJob is not serialised against
        // this job — archive an ad between them and the email announces FEWER ads than it lists, trading
        // the lie this PR removes for a rarer one. Capping BEFORE the count would floor the total at
        // MaxItemsPerDigest and silently delete the "och N till" remainder — a third way this one number
        // can lie, and the arm it lives in had no spec at all until code-reviewer said so.
        var claimedAdIds = effective.Select(h => h.JobAdId).ToHashSet();
        var claimRows = presentableRows
            .Where(r => claimedAdIds.Contains(r.JobAdId))
            .ToList();

        var presentableTotal = claimRows.Count;
        var items = claimRows
            .Take(_options.MaxItemsPerDigest)
            .Select(r => new FollowedCompanyAdItem(r.Title, r.Company))
            .ToList();

        // Claim the EFFECTIVE follow rows (Pending → Queued) and commit BEFORE the send — the
        // idempotency spine (parity the match digest; single-threaded, DisableConcurrentExecution).
        // `effective` is claim ∪ drain: the drain rows are consumed here too, because they can never be
        // shown and nothing else would ever reap them. Only the stayPending class — presentable, graded
        // out — is left untouched, which is exactly what 8C's retroactive re-surfacing needs.
        foreach (var hit in effective)
            hit.MarkQueued();
        await db.SaveChangesAsync(ct);

        var toEmail = await userAccounts.GetEmailAsync(userId, ct);
        if (string.IsNullOrWhiteSpace(toEmail))
        {
            // Orphan consent row without an account email — claimed rows stay Queued (TD-114 posture).
            LogFollowNoEmail(logger, userId);
            return false;
        }

        if (items.Count == 0)
        {
            // Nothing PRESENTABLE among the claimed hits — every one of their ads' rows is gone, or
            // (since #864) archived. Drain the claimed set (mark Sent) so the empty window doesn't
            // re-process every run; send nothing (an empty digest is noise). The stayPending class is not
            // in `effective` and is untouched here — a graded-out hit is not consumed by an empty window.
            DrainSent(effective);
            await db.SaveChangesAsync(ct);
            LogFollowEmptyDrained(logger, effective.Count, userId);
            return false;
        }

        var content = new FollowedCompanyNotificationEmail(
            cadence, items, presentableTotal, filterSummary);

        // Idempotency key: CONTENT fingerprint of the CLAIMED hit set (namespaced follow/v1/…), NOT a
        // wall-clock window — two same-period runs that claimed different sets get different keys.
        var idempotencyKey = FollowedCompanyNotificationIdempotencyKey.ForDigest(
            userId, cadence, effective.Select(h => h.Id.Value));

        try
        {
            await emailSender.SendFollowedCompanyNotificationEmailAsync(
                toEmail, content, idempotencyKey, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Send failed AFTER the claim → rows stay Queued, never re-sent ("never double-email >
            // never miss"). No rethrow — the hits persist; only this run's email is missed.
            LogFollowSendFailed(logger, ex, userId);
            return false;
        }

        // Drain: mark ALL claimed window rows Sent (not just the displayed cap) so the un-displayed
        // remainder cannot re-surface next digest.
        DrainSent(effective);
        await db.SaveChangesAsync(ct);
        LogFollowSent(logger, userId, presentableTotal, items.Count, effective.Count);
        return true;
    }

    // Read-time ≥Good membership for the OnlyMatched filter (RF-5=5A fixed floor). Builds the user's
    // DEK-free Fast profile (the Worker has no ICurrentUser → BuildFullForUserIdAsync) and delegates to
    // 13B (RF-13) + CTO sub-bind A′ — aggregate ALL the user's ACTIVE watch filters into a standing
    // disclosure (booleans only; no ort names, no grade — D1/Goodhart safe). The domain of the
    // quantifier is deliberately every active watch, matching the rendered sentence ("ett eller flera
    // av företagen du följer"): the disclosure is a caveat about the user's SETTINGS ("you have filters
    // active, so ads may be missing"), not a report of what this particular email dropped. The
    // event-scoped alternative is not merely narrower, it is unknowable for the ort axis — 8A never
    // creates the hit row, so nothing records what was suppressed.
    //
    // The ort filter always narrows (scan-time, 8A) so it discloses unconditionally. OnlyMatched
    // narrows only when the user is assessable (a profile-less filter is INERT), so it discloses only
    // then. Null when the user has no active, effective filter — and under A′ that null is now a TRUE
    // claim ("none of the companies you follow is filtered"), which it was not before.
    private static FollowedCompanyFilterSummary? BuildFilterSummary(
        Dictionary<CompanyWatchId, WatchFilterSpec?> filterByWatchId,
        bool onlyMatchedAssessable)
    {
        var onlyMatched = false;
        var location = false;
        foreach (var filter in filterByWatchId.Values)
        {
            if (filter is null)
                continue;
            if (filter.OnlyMatched && onlyMatchedAssessable)
                onlyMatched = true;

            // BOTH geo axes count as an active ort filter (F4a). A whole-län watch carries its
            // selection on the REGION axis and leaves Municipalities EMPTY (län is never expanded
            // into kommun-ids — see WatchFilterSpec). Reading only the kommun axis here would make
            // a "Hela Skåne"-watch look unfiltered: the scan would still suppress hits (8A) while
            // the email disclosed nothing — exactly the silent narrowing RF-13 rejected.
            if (filter.Municipalities.Count > 0 || filter.Regions.Count > 0)
                location = true;
        }

        return onlyMatched || location
            ? new FollowedCompanyFilterSummary(onlyMatched, location)
            : null;
    }

    // Queued → Sent for the whole claimed follow-hit batch (parity the match DrainSent).
    private void DrainSent(IReadOnlyList<FollowedCompanyAdHit> hits)
    {
        foreach (var hit in hits)
            hit.MarkSent(clock);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "DigestDispatchJob: {Cadence} — {Count} consenting users due")]
    private static partial void LogDue(ILogger logger, DigestCadence cadence, int count);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "DigestDispatchJob: digest failed for user {UserId} — isolated, will retry next run")]
    private static partial void LogUserFailed(ILogger logger, Exception ex, Guid userId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "DigestDispatchJob: no account email for user {UserId} — skipped (rows left Queued)")]
    private static partial void LogNoEmail(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "DigestDispatchJob: no presentable ad among {Count} matches for user {UserId} " +
                  "(rows gone or archived) — drained, no email")]
    private static partial void LogEmptyDrained(ILogger logger, int count, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "DigestDispatchJob: send failed for user {UserId} — rows left Queued (no double-send)")]
    private static partial void LogSendFailed(ILogger logger, Exception ex, Guid userId);

    // #864 — {Presentable} and {Claimed} are logged SEPARATELY on purpose. They used to be ONE number
    // ({Total} = the claimed set), which is a large part of why nobody saw this bug: the log agreed with
    // itself while the email announced a set it could not show. Their DIVERGENCE is the signal — a
    // healthy run has Claimed == Presentable; a run where they differ silently drained matches whose ads
    // had gone archived. This is a defect competent telemetry would have surfaced years earlier.
    [LoggerMessage(Level = LogLevel.Information,
        Message = "DigestDispatchJob: digest sent to user {UserId} — {Presentable} presentable strong " +
                  "matches ({Displayed} shown), {Claimed} claimed and drained")]
    private static partial void LogSent(ILogger logger, Guid userId, int presentable, int displayed, int claimed);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "DigestDispatchJob: done — {Cadence}, {Processed} users processed, {Sent} digests sent")]
    private static partial void LogComplete(ILogger logger, DigestCadence cadence, int processed, int sent);

    // ─── Company-follow digest pass log messages (ADR 0087 D5). NEVER carry an org.nr (D8/§5) —
    // only counts + opaque user ids (parity the match pass).
    [LoggerMessage(Level = LogLevel.Information,
        Message = "DigestDispatchJob (follow): {Cadence} — {Count} consenting users due")]
    private static partial void LogFollowDue(ILogger logger, DigestCadence cadence, int count);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "DigestDispatchJob (follow): digest failed for user {UserId} — isolated, will retry next run")]
    private static partial void LogFollowUserFailed(ILogger logger, Exception ex, Guid userId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "DigestDispatchJob (follow): no account email for user {UserId} — skipped (rows left Queued)")]
    private static partial void LogFollowNoEmail(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "DigestDispatchJob (follow): no presentable ad among {Count} claimed hits for user " +
                  "{UserId} (rows gone or archived) — drained, no email")]
    private static partial void LogFollowEmptyDrained(ILogger logger, int count, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "DigestDispatchJob (follow): send failed for user {UserId} — rows left Queued (no double-send)")]
    private static partial void LogFollowSendFailed(ILogger logger, Exception ex, Guid userId);

    // #864 — parity the match digest: {Presentable} and {Claimed} are separate, and their DIVERGENCE is
    // the operational signal that pending hits were drained without ever being shown (their ads went
    // archived, or their rows are gone).
    [LoggerMessage(Level = LogLevel.Information,
        Message = "DigestDispatchJob (follow): digest sent to user {UserId} — {Presentable} presentable " +
                  "new ads ({Displayed} shown), {Claimed} claimed and drained")]
    private static partial void LogFollowSent(ILogger logger, Guid userId, int presentable, int displayed, int claimed);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "DigestDispatchJob (follow): done — {Cadence}, {Processed} users processed, {Sent} digests sent")]
    private static partial void LogFollowComplete(ILogger logger, DigestCadence cadence, int processed, int sent);
}
