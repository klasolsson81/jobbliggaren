using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.Matching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Application.Matching.Jobs.StrandedMatchReaper;

/// <summary>
/// TD-114 (ADR 0080 Vag 4) — scheduled reaper for stranded notification rows on BOTH rails: skill
/// <see cref="UserJobAdMatch"/> rows AND (#453 audit #26) company-follow
/// <see cref="Domain.CompanyWatches.FollowedCompanyAdHit"/> rows. The Top-direct
/// (<c>BackgroundMatchingJob</c>), match-digest and follow-digest (<c>DigestDispatchJob</c>) dispatch
/// paths all use the same claim-then-send spine (<c>Pending → MarkQueued</c> + commit → send →
/// <c>MarkSent</c> + commit). If the send never completes (a Resend failure, or the account had
/// no email) the row stays permanently Queued with no recovery — the deliberate "never double-email
/// &gt; never miss" MVP trade-off, conditional (ADR 0080) on this reaper existing before the
/// prod-Resend flip.
/// <para>
/// <b>Two arms, one mechanism (#453 audit #26 — DRY):</b> the follow rail (ADR 0087 D5) re-shipped the
/// identical claim-then-send strand class, so this job was EXTENDED with a second typed query (not a
/// parallel job): the threshold, the <c>Queued → Failed</c> transition, the single atomic save, and the
/// 04:45 schedule are one shared mechanism across two aggregates that carry different data (a follow hit
/// has no grade).
/// </para>
/// <para>
/// <b>Strategy (senior-cto-advisor): MarkFailed, never re-send.</b> A row Queued past the
/// threshold is moved to the terminal <see cref="NotificationStatus.Failed"/> state — it becomes
/// observable + queryable, is NEVER re-sent (honouring the dedup stance), and is safe against the
/// non-dev <c>NullEmailSender</c> (no send is attempted, so a still-broken Resend config can never
/// be masked as a false delivery). The missing-account-email strand and the transient-send-failure
/// strand both terminate here (the original reason is not stored — distinguishing them would need
/// a new column / migration, out of scope; the reap is logged either way).
/// </para>
/// <para>
/// <b>Aging by <c>CreatedAt</c> (no migration).</b> No <c>QueuedAt</c> column is added on either
/// aggregate. <c>CreatedAt</c> predates the claim (<c>CreatedAt ≤ QueuedAt</c>), so aging by it is
/// AGGRESSIVE, not conservative (audit #37 truth-fix — the prior comment had this inverted): the 48h
/// threshold on <c>CreatedAt</c> is reached BEFORE 48h of actual Queued-time strandedness, so the reaper
/// may act EARLIER than the true strand age, never later. That is safe: a reaped row was genuinely
/// stranded (the digest re-queries only Pending, so a Queued row is terminal after a failed send — an
/// early reap yields the same correct Failed label, just sooner, and never resends). It does mean a
/// weekly-cadence row created days before its send can be reaped shortly after that send fails; still
/// correct (it was stranded). The <c>Failed</c> value is stored by name in the existing
/// <c>varchar(20)</c> status column on both tables (no migration).
/// </para>
/// <para>
/// <b>Atomic + idempotent.</b> <see cref="UserJobAdMatch.MarkFailed"/> is a pure in-memory,
/// guard-safe transition (only a Queued row can fail, and the query already filters Queued), so
/// the only failure mode is a transient DB error affecting the whole batch. A single atomic
/// <see cref="IAppDbContext.SaveChangesAsync"/> + the nightly retry is the resilient choice: a
/// failed run rolls back wholly and is picked up by the next run (every row is still Queued).
/// Registered at 04:45 UTC (after the PREVIOUS cycle's digest windows settle, inside the quiet
/// hard-delete cluster), <c>DisableConcurrentExecution</c>-guarded. NO AI/LLM (ADR 0071).
/// </para>
/// <para>
/// <b>Issue #184 AC "per-row failure isolation" — satisfied in spirit (CTO 2026-06-25).</b> The
/// AC's intent is "one strand must not permanently block reaping the others"; batch-atomic +
/// idempotent-nightly-retry meets it because no row is ever permanently stuck. On THIS schema
/// the only non-transient single-row failure is a row that vanished between the query and the
/// save (Art.17 cascade / JobAd-expiry) — which SELF-RESOLVES next run (it is gone from the
/// <c>Where(Queued)</c> query). A permanent single-row poison cannot exist without a concurrency
/// token or a per-row status constraint, of which there are none. This deliberately differs from
/// <c>HardDeleteAccountsJob</c>'s per-row try/catch: that job isolates INDEPENDENT EXTERNAL
/// operations (Identity delete + cascade via a port owning its own transaction); the reaper's
/// per-row work is a single in-memory status flip with no external dependency — same-looking
/// loop, different change-reason. Per-row-with-<see cref="IAppDbContext.Detach"/> was available
/// and rejected on merit (it would defend against a poison this schema cannot produce).
/// </para>
/// </summary>
public sealed partial class StrandedMatchReaperJob(
    IAppDbContext db,
    IDateTimeProvider clock,
    ILogger<StrandedMatchReaperJob> logger)
{
    /// <summary>
    /// A row must sit Queued at least this long before the reaper fails it. Generous on purpose: a
    /// DAILY-cadence row is legitimately Queued only within a single dispatch window (the scan at
    /// ~03:20 UTC → dispatch at 06:00 UTC, sub-day), so 48h clears any same-/next-day legitimate
    /// in-flight state with margin. (Audit #37 truth-note: a WEEKLY-cadence row can sit Pending for
    /// days before its send, so — because aging is by <c>CreatedAt</c>, see the class doc — a weekly
    /// row whose send fails may be reaped after &lt; 48h of actual Queued time. That is still correct:
    /// the row was genuinely stranded and reaping never resends. The reaper does not race a
    /// slow-but-succeeding send in practice — the shipped dispatch crons commit MarkSent before this
    /// 04:45 run and a Queued row is only produced by a FAILED send, which does not later succeed.)
    /// Hardcoded in this phase — flip to IOptions if policy changes (HardDeleteAccountsJob precedent).
    /// </summary>
    private const int StrandedThresholdHours = 48;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var cutoff = clock.UtcNow.AddHours(-StrandedThresholdHours);

        // Two arms, mutated on the SAME tracked context, committed by ONE atomic SaveChanges below
        // (#453 audit #26 — the follow rail joins the match rail; see the class doc).
        var reapedMatches = await ReapStrandedMatchesAsync(cutoff, cancellationToken);
        var reapedFollowHits = await ReapStrandedFollowHitsAsync(cutoff, cancellationToken);

        if (reapedMatches == 0 && reapedFollowHits == 0)
            return;

        // One atomic SaveChanges across BOTH rails — in-memory guard-safe transitions, so the only
        // failure is a transient batch-level DB error, fully rolled back and retried next run (every
        // row still Queued).
        await db.SaveChangesAsync(cancellationToken);

        // Gate each completion log on its own arm so a follow-only run does not emit a noisy
        // "0 matchningar markerade Failed" (parity the follow-arm gate; dotnet-architect 2026-07-02).
        if (reapedMatches > 0)
            LogComplete(logger, reapedMatches);
        if (reapedFollowHits > 0)
            LogFollowComplete(logger, reapedFollowHits);
    }

    // Skill-match rail (the original TD-114 concern). Marks (does not save) stranded rows.
    private async Task<int> ReapStrandedMatchesAsync(DateTimeOffset cutoff, CancellationToken ct)
    {
        // Tracking is deliberate (write path — the rows are mutated + saved), NOT a §3.6
        // AsNoTracking read. Queued rows are not soft-deleted (DeletedAt null) so the default
        // query filter includes them; a Queued row soft-deleted by JobAd-expiry / the Art.17
        // cascade is correctly excluded — we never reap an already-removed match. NOT paginated:
        // the stranded set is bounded by design (a row is legitimately Queued only within a
        // sub-day dispatch window, and only FAILED sends strand) — a Take/chunk is deferred to
        // the day a fitness signal flags the transaction size (ADR 0045; CTO 2026-06-25).
        var stranded = await db.UserJobAdMatches
            .Where(m => m.NotificationStatus == NotificationStatus.Queued && m.CreatedAt < cutoff)
            .ToListAsync(ct);

        LogStrandedFound(logger, stranded.Count, cutoff);
        if (stranded.Count == 0)
            return 0;

        var reaped = 0;
        foreach (var match in stranded)
        {
            ct.ThrowIfCancellationRequested();
            // The query already filtered Queued, so MarkFailed always succeeds — but honour the
            // domain Result and count only actual transitions so the completion log is truthful.
            if (match.MarkFailed().IsFailure)
                continue;

            reaped++;
            // PII-free: the match id + named grade + age, never the recipient address.
            LogReaped(logger, match.Id.Value, match.Grade.ToString(), match.CreatedAt);
        }

        return reaped;
    }

    // #453 (audit #26) — company-follow rail. Same strand mechanism, different aggregate
    // (FollowedCompanyAdHit has no grade). Extending this job (not a parallel job) is DRY: the
    // threshold / Queued→Failed / atomic save / 04:45 schedule are one shared mechanism. The follow
    // soft-delete query filter excludes hits removed by JobAd-expiry / the Art.17 cascade. Bounded by
    // the same sub-day-dispatch-window design; org.nr is never read here (PII-free — id + age only).
    private async Task<int> ReapStrandedFollowHitsAsync(DateTimeOffset cutoff, CancellationToken ct)
    {
        var stranded = await db.FollowedCompanyAdHits
            .Where(h => h.NotificationStatus == FollowedCompanyAdHitStatus.Queued && h.CreatedAt < cutoff)
            .ToListAsync(ct);

        LogFollowStrandedFound(logger, stranded.Count, cutoff);
        if (stranded.Count == 0)
            return 0;

        var reaped = 0;
        foreach (var hit in stranded)
        {
            ct.ThrowIfCancellationRequested();
            if (hit.MarkFailed().IsFailure)
                continue;

            reaped++;
            LogFollowReaped(logger, hit.Id.Value, hit.CreatedAt);
        }

        return reaped;
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "StrandedMatchReaperJob: hittade {Count} matchningar köade längre än tröskeln (cutoff {Cutoff:yyyy-MM-ddTHH:mm:ssZ})")]
    private static partial void LogStrandedFound(ILogger logger, int count, DateTimeOffset cutoff);

    [LoggerMessage(EventId = 2601, Level = LogLevel.Warning,
        Message = "StrandedMatchReaperJob: markerar strandad match {MatchId} (grad {Grade}, skapad {CreatedAt:yyyy-MM-ddTHH:mm:ssZ}) som Failed")]
    private static partial void LogReaped(ILogger logger, Guid matchId, string grade, DateTimeOffset createdAt);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "StrandedMatchReaperJob: klart — {Count} strandade matchningar markerade Failed")]
    private static partial void LogComplete(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "StrandedMatchReaperJob: hittade {Count} företagsträffar köade längre än tröskeln (cutoff {Cutoff:yyyy-MM-ddTHH:mm:ssZ})")]
    private static partial void LogFollowStrandedFound(ILogger logger, int count, DateTimeOffset cutoff);

    [LoggerMessage(EventId = 2602, Level = LogLevel.Warning,
        Message = "StrandedMatchReaperJob: markerar strandad företagsträff {HitId} (skapad {CreatedAt:yyyy-MM-ddTHH:mm:ssZ}) som Failed")]
    private static partial void LogFollowReaped(ILogger logger, Guid hitId, DateTimeOffset createdAt);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "StrandedMatchReaperJob: klart — {Count} strandade företagsträffar markerade Failed")]
    private static partial void LogFollowComplete(ILogger logger, int count);
}
