using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Matching.Notifications;
using Jobbliggaren.Domain.Common;
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
}
