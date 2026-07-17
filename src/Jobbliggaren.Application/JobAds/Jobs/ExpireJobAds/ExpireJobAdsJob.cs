using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Application.JobAds.Jobs.ExpireJobAds;

/// <summary>
/// Hangfire RecurringJob (cron <c>45 3 * * *</c> per architect-design 2026-05-23).
/// Defense-in-depth-pass: arkiverar JobAds vars <c>ExpiresAt</c> har passerat
/// <c>UtcNow</c>. Snapshot-miss-retention räcker normalt (utgångna annonser
/// faller ur snapshot → miss-räknare tickar → arkivering), men expiry-cron
/// fångar:
/// <list type="bullet">
/// <item>Annonser där JobTech-utgång inträffar utan stream-removal-event (race-fönster).</item>
/// <item>Manuella JobAds (Source=Manual) med satt ExpiresAt — utanför snapshot-vägen.</item>
/// <item>Defense-in-depth om snapshot-jobbet trasas 3+ dygn.</item>
/// </list>
/// <para>
/// Bulk-UPDATE via <c>ExecuteUpdateAsync</c> — domain-event raisas EJ per item
/// (CTO-rond 2026-05-23 Q3=B). Aggregerad audit-rad via <see cref="ISystemEventAuditor"/>.
/// </para>
/// <para>
/// Idempotent: andra körningen samma dygn hittar 0 rader (alla expired-Active
/// blev Archived första gången). Vid ExpiresAt=NULL eller ExpiresAt>now hoppas
/// raden över.
/// </para>
/// </summary>
public sealed partial class ExpireJobAdsJob(
    IAppDbContext db,
    IDateTimeProvider clock,
    ISystemEventAuditor auditor,
    ILogger<ExpireJobAdsJob> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid();
        var startedAt = clock.UtcNow;
        LogStarted(logger);

        // SetProperty på SmartEnum-converter fungerar med statisk readonly-värde.
        // JobAd har inget query-filter (#821 retirerade den döda soft-delete-axeln):
        // Status == Active i Where-satsen nedan ÄR hela avgränsningen, explicit.
        //
        // ALLOW-LIST (`== Active`), never `!= Archived` — and on THIS writer the deny-list is
        // worse than a leak: Erase() does not touch ExpiresAt, so a `!= Archived` selection
        // re-stamps an expired Erased tombstone (#842) to Archived, bypassing the aggregate's
        // Archive() guard (ExecuteUpdate never loads the aggregate) — and UpdateFromSource's
        // re-import refusal keys on Status == Erased, so the erased ad walks back in on the next
        // nightly sync. WITNESSED (CTO 2026-07-16 B7, resequenced by 2026-07-17 R2/R3 once PR #911
        // supplied the real-Postgres fixture): the real-SUT test
        // RecruiterContactRetentionTests.ExpireJobAdsJob_does_not_resurrect_an_expired_Erased_tombstone
        // seeds a real Erased tombstone (JobAd.Import + Erase(), production funnel) with a past
        // ExpiresAt and proves it is excluded from this bulk selection — the resurrection mutant
        // (`== Active` → `!= Archived`) goes RED there against real Postgres.
        var archivedStatus = JobAdStatus.Archived;
        var rowsAffected = await db.JobAds
            .Where(j => j.Status == JobAdStatus.Active
                        && j.ExpiresAt != null
                        && j.ExpiresAt < startedAt)
            .ExecuteUpdateAsync(
                // #842 Tier A retention (re-bind R4, b1 §4.2): the bulk path bypasses the
                // aggregate, so the contact clear Archive() performs must be repeated HERE — one
                // of the THREE archival writers the fitness test ("no non-Active ad holds a
                // contact") binds together. The typed null cast is what makes the
                // converter-mapped jsonb SetProperty translate.
                s => s
                    .SetProperty(j => j.Status, _ => archivedStatus)
                    .SetProperty(j => j.Contacts, _ => (AdContacts?)null),
                cancellationToken).ConfigureAwait(false);

        var completedAt = clock.UtcNow;
        LogCompleted(logger, rowsAffected, (completedAt - startedAt).TotalSeconds);

        await auditor.RecordAsync(new JobAdsRetentionCompleted(
            AggregateId: runId,
            OccurredAt: completedAt,
            Source: "all",
            Reason: "expired",
            ArchivedCount: rowsAffected,
            Threshold: null,
            ParsedTotalLastSnapshot: null,
            Max7dObservedSnapshot: null,
            ThresholdAborted: false,
            AbortReason: null,
            StartedAt: startedAt,
            CompletedAt: completedAt), cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(EventId = 5801, Level = LogLevel.Information,
        Message = "ExpireJobAdsJob: startad — söker Active-rader med ExpiresAt < now.")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(EventId = 5802, Level = LogLevel.Information,
        Message = "ExpireJobAdsJob: klart — archived={ArchivedCount}, durationSec={DurationSec}.")]
    private static partial void LogCompleted(ILogger logger, int archivedCount, double durationSec);
}
