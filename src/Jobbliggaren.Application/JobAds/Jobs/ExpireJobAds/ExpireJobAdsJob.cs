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
        var archivedStatus = JobAdStatus.Archived;
        var rowsAffected = await db.JobAds
            .Where(j => j.Status == JobAdStatus.Active
                        && j.ExpiresAt != null
                        && j.ExpiresAt < startedAt)
            .ExecuteUpdateAsync(
                s => s.SetProperty(j => j.Status, _ => archivedStatus),
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
