using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Matching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Application.Matching.Jobs.StrandedMatchReaper;

/// <summary>
/// TD-114 (ADR 0080 Vag 4) — scheduled reaper for stranded <see cref="UserJobAdMatch"/> rows.
/// The Top-direct (<c>BackgroundMatchingJob</c>) and digest (<c>DigestDispatchJob</c>) dispatch
/// paths use a claim-then-send spine (<c>Pending → MarkQueued</c> + commit → send →
/// <c>MarkSent</c> + commit). If the send never completes (a Resend failure, or the account had
/// no email) the row stays permanently <see cref="NotificationStatus.Queued"/> with no recovery
/// — the deliberate "never double-email &gt; never miss" MVP trade-off, conditional (ADR 0080)
/// on this reaper existing before the prod-Resend flip.
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
/// <b>Aging by <see cref="UserJobAdMatch.CreatedAt"/> (no migration).</b> No <c>QueuedAt</c> column
/// is added — <c>CreatedAt</c> predates the claim, so aging by it is conservative (the reaper acts
/// no earlier than the true strand age, never later). The <see cref="NotificationStatus.Failed"/>
/// value is stored by name in the existing <c>varchar(20)</c> column (no migration).
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
/// </summary>
public sealed partial class StrandedMatchReaperJob(
    IAppDbContext db,
    IDateTimeProvider clock,
    ILogger<StrandedMatchReaperJob> logger)
{
    /// <summary>
    /// A match must sit Queued at least this long before the reaper fails it. Generous on
    /// purpose: a row is legitimately Queued only within a single dispatch window (the scan at
    /// ~03:20 UTC → dispatch at 06:00 UTC, sub-day), so 48h clears any same-/next-day legitimate
    /// in-flight state with margin and can never race a slow-but-succeeding send. Hardcoded in
    /// this phase — flip to IOptions if policy changes (HardDeleteAccountsJob precedent).
    /// </summary>
    private const int StrandedThresholdHours = 48;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var cutoff = clock.UtcNow.AddHours(-StrandedThresholdHours);

        // Queued rows are not soft-deleted (DeletedAt null) so the default query filter includes
        // them; a Queued row soft-deleted by JobAd-expiry / the Art.17 cascade is correctly
        // excluded — we never reap an already-removed match.
        var stranded = await db.UserJobAdMatches
            .Where(m => m.NotificationStatus == NotificationStatus.Queued && m.CreatedAt < cutoff)
            .ToListAsync(cancellationToken);

        LogStrandedFound(logger, stranded.Count, cutoff);

        if (stranded.Count == 0)
            return;

        foreach (var match in stranded)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Guard cannot fail (the query filtered Queued), but respect the domain result.
            if (match.MarkFailed().IsFailure)
                continue;

            // PII-free: the match id + named grade + age, never the recipient address.
            LogReaped(logger, match.Id.Value, match.Grade.ToString(), match.CreatedAt);
        }

        // One atomic SaveChanges — see the class doc (in-memory guard-safe transition → the only
        // failure is a transient batch-level DB error, fully rolled back and retried next run).
        await db.SaveChangesAsync(cancellationToken);

        LogComplete(logger, stranded.Count);
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
}
