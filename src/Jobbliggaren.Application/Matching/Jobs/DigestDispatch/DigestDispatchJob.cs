using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Matching.Notifications;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
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
/// <b>Consent is the query gate (GDPR Art. 6/7):</b> opt-in ON and not withdrawn — identical to the
/// scan. A withdrawal stops dispatch immediately (its Pending rows are simply never picked up).
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
/// <see cref="DigestDispatchOptions.MaxItemsPerDigest"/> items while the honest
/// <c>TotalCount</c> reports the full window. ALL window rows are drained (marked Sent), not just
/// the displayed cap, so the remainder cannot re-surface next digest.
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
    IDateTimeProvider clock,
    IOptions<DigestDispatchOptions> options,
    ILogger<DigestDispatchJob> logger)
{
    private readonly DigestDispatchOptions _options = options.Value;

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
        // same ordering → the most recent N of `pending`. The inner join honours the JobAd
        // soft-delete query filter (DeletedAt == null), so a row whose ad was soft-deleted/erased
        // since the match falls out of the body but is still drained below (it was a valid match
        // when detected). No Status==Active predicate here — deliberate parity with the in-app
        // /matchningar surface (GetMyMatchesQueryHandler), so the email shows the SAME set the user
        // sees in-app. Joining (not filtering by an id set) also sidesteps the strongly-typed-VO
        // Contains translation trap.
        var displayRows = await (
                from m in db.UserJobAdMatches.AsNoTracking()
                where m.UserId == userId
                      && m.Grade == NotifiableMatchGrade.Strong
                      && m.NotificationStatus == NotificationStatus.Pending
                join j in db.JobAds.AsNoTracking() on m.JobAdId equals j.Id
                orderby m.CreatedAt descending, m.Id
                select new { j.Title, Company = j.Company.Name })
            .Take(_options.MaxItemsPerDigest)
            .ToListAsync(ct);

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
            // Every matched ad was retracted since detection — nothing to show. Drain (mark Sent)
            // so the empty window doesn't re-process every run; send nothing (an empty digest is
            // noise, not a notification).
            DrainSent(pending);
            await db.SaveChangesAsync(ct);
            LogEmptyDrained(logger, pending.Count, userId);
            return false;
        }

        // Strong is the only grade in this batch (the query filters it) → "Stark match" for every
        // item. The honest TotalCount is the full window (pending.Count); the body lists the cap
        // and renders "och N till" for the remainder.
        var items = displayRows
            .Select(r => new MatchNotificationItem(
                r.Title, r.Company, NotifiableMatchGrade.Strong.ToSwedishLabel()))
            .ToList();
        var content = new MatchNotificationEmail(
            MatchNotificationKind.Digest, cadence, items, pending.Count);

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
        LogSent(logger, userId, pending.Count, items.Count);
        return true;
    }

    // Queued → Sent for the whole claimed batch. MarkSent's Result is structurally Success (the
    // rows were just claimed Queued).
    private void DrainSent(IReadOnlyList<UserJobAdMatch> matches)
    {
        foreach (var match in matches)
            match.MarkSent(clock);
    }

    // ─── Company-follow digest (ADR 0087 D5) — the exact SHAPE of DispatchUserDigestAsync above, but
    // over FollowedCompanyAdHit rows (no grade) + the FollowedCompanyNotificationEmail contract. Kept
    // as a SEPARATE method so the two aggregate sources never share fetch/dispatch state (SoC).
    private async Task<bool> DispatchUserFollowedCompanyDigestAsync(
        Guid userId, DigestCadence cadence, CancellationToken ct)
    {
        // The user's Pending follow-hit rows (tracked — MarkQueued/MarkSent mutate them). Ordered by
        // recency (CreatedAt desc, then Id for determinism) — no grade concept for follows.
        // #453 (cross-channel dedup) — AND SeenAt == null: a hit the user already opened in-app is
        // suppressed ("aldrig mejla något jag sett i appen"). A stamped-but-Pending hit is never claimed
        // here (falls dormant) and the scan's triple-dedup never re-creates it. This predicate MUST match
        // the displayRows fetch below, or the claimed set would diverge from the displayed set.
        var pending = await db.FollowedCompanyAdHits
            .Where(h => h.UserId == userId
                        && h.NotificationStatus == FollowedCompanyAdHitStatus.Pending
                        && h.SeenAt == null)
            .OrderByDescending(h => h.CreatedAt)
            .ThenBy(h => h.Id)
            .ToListAsync(ct);

        if (pending.Count == 0)
            return false;

        // Display items (capped) joined to each ad's PUBLIC title/company (never the org.nr — ADR
        // 0087 D8). Read BEFORE the claim, AsNoTracking, same ordering. The inner join honours the
        // JobAd soft-delete query filter, so a retracted ad falls out of the body but is still
        // drained below. Joining (not filtering by an id set) sidesteps the strongly-typed-VO
        // Contains trap (parity the match digest).
        var displayRows = await (
                from h in db.FollowedCompanyAdHits.AsNoTracking()
                where h.UserId == userId
                      && h.NotificationStatus == FollowedCompanyAdHitStatus.Pending
                      // #453 — MUST mirror the `pending` claim predicate above (suppress seen-in-app hits).
                      && h.SeenAt == null
                join j in db.JobAds.AsNoTracking() on h.JobAdId equals j.Id
                orderby h.CreatedAt descending, h.Id
                select new { j.Title, Company = j.Company.Name })
            .Take(_options.MaxItemsPerDigest)
            .ToListAsync(ct);

        // Claim ALL pending follow rows (Pending → Queued) and commit BEFORE the send — the
        // idempotency spine (parity the match digest; single-threaded, DisableConcurrentExecution).
        foreach (var hit in pending)
            hit.MarkQueued();
        await db.SaveChangesAsync(ct);

        var toEmail = await userAccounts.GetEmailAsync(userId, ct);
        if (string.IsNullOrWhiteSpace(toEmail))
        {
            // Orphan consent row without an account email — claimed rows stay Queued (TD-114 posture).
            LogFollowNoEmail(logger, userId);
            return false;
        }

        if (displayRows.Count == 0)
        {
            // Every followed-ad was retracted since detection — drain (mark Sent) so the empty window
            // doesn't re-process every run; send nothing (an empty digest is noise).
            DrainSent(pending);
            await db.SaveChangesAsync(ct);
            LogFollowEmptyDrained(logger, pending.Count, userId);
            return false;
        }

        var items = displayRows
            .Select(r => new FollowedCompanyAdItem(r.Title, r.Company))
            .ToList();
        var content = new FollowedCompanyNotificationEmail(cadence, items, pending.Count);

        // Idempotency key: CONTENT fingerprint of the claimed hit set (namespaced follow/v1/…), NOT a
        // wall-clock window — two same-period runs that claimed different sets get different keys.
        var idempotencyKey = FollowedCompanyNotificationIdempotencyKey.ForDigest(
            userId, cadence, pending.Select(h => h.Id.Value));

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

        // Drain: mark ALL window rows Sent (not just the displayed cap) so the un-displayed remainder
        // cannot re-surface next digest.
        DrainSent(pending);
        await db.SaveChangesAsync(ct);
        LogFollowSent(logger, userId, pending.Count, items.Count);
        return true;
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
        Message = "DigestDispatchJob: all {Count} matched ads retracted for user {UserId} — drained, no email")]
    private static partial void LogEmptyDrained(ILogger logger, int count, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "DigestDispatchJob: send failed for user {UserId} — rows left Queued (no double-send)")]
    private static partial void LogSendFailed(ILogger logger, Exception ex, Guid userId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "DigestDispatchJob: digest sent to user {UserId} — {Total} strong matches ({Displayed} shown)")]
    private static partial void LogSent(ILogger logger, Guid userId, int total, int displayed);

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
        Message = "DigestDispatchJob (follow): all {Count} followed ads retracted for user {UserId} — drained, no email")]
    private static partial void LogFollowEmptyDrained(ILogger logger, int count, Guid userId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "DigestDispatchJob (follow): send failed for user {UserId} — rows left Queued (no double-send)")]
    private static partial void LogFollowSendFailed(ILogger logger, Exception ex, Guid userId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "DigestDispatchJob (follow): digest sent to user {UserId} — {Total} new ads ({Displayed} shown)")]
    private static partial void LogFollowSent(ILogger logger, Guid userId, int total, int displayed);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "DigestDispatchJob (follow): done — {Cadence}, {Processed} users processed, {Sent} digests sent")]
    private static partial void LogFollowComplete(ILogger logger, DigestCadence cadence, int processed, int sent);
}
