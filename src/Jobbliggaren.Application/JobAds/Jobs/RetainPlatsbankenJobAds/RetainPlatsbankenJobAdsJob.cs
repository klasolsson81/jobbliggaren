using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Application.JobAds.Jobs.RetainPlatsbankenJobAds;

/// <summary>
/// Hangfire RecurringJob (cron <c>15 3 * * *</c> per architect-design 2026-05-23
/// — efter snapshot-fönstret 02:00 + audit-log-retention 03:00). Arkiverar
/// Platsbanken-JobAds vars <c>JobAdSnapshotMiss.MissCount</c> har nått
/// <see cref="JobSourceRetentionOptions.SnapshotMissThreshold"/> (default 3 =
/// N=3 konsekutiva snapshot-misses, CTO-rond 2026-05-23 Q5).
/// <para>
/// <b>Defense-in-depth uppströms</b> (snapshot-jobbet skippar miss-tracking vid
/// trunkering/floor-brott) + <b>post-archive circuit-breaker</b> här (CTO-rond
/// 2026-05-23 H1 + security-auditor). Pre-archive COUNT jämför candidates/active
/// mot <see cref="JobSourceRetentionOptions.MaxArchivePctPerRun"/> (default 0.25)
/// — vid överskridning: ABORT (audit-rad med <c>ThresholdAborted=true</c>,
/// <c>AbortReason="max-archive-pct-exceeded"</c>) + throw
/// <see cref="DomainException"/> för fail-loud (Hangfire-retry → CloudWatch alarm).
/// </para>
/// <para>
/// Bulk-UPDATE via <see cref="IJobAdSnapshotMissTracker.ArchiveJobAdsWithMissCountAtLeastAsync"/>
/// — domain-event raisas EJ per item (CTO Q3=B). Aggregerad audit-rad via
/// <see cref="ISystemEventAuditor"/> är retention-vägens accountability-spår
/// (GDPR Art. 30).
/// </para>
/// </summary>
public sealed partial class RetainPlatsbankenJobAdsJob(
    IJobAdSnapshotMissTracker missTracker,
    IOptions<JobSourceRetentionOptions> retentionOptions,
    IDateTimeProvider clock,
    ISystemEventAuditor auditor,
    ILogger<RetainPlatsbankenJobAdsJob> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid();
        var startedAt = clock.UtcNow;
        var opts = retentionOptions.Value;
        var threshold = opts.SnapshotMissThreshold;
        var maxPct = opts.MaxArchivePctPerRun;
        var source = JobSource.Platsbanken;

        LogStarted(logger, source.Value, threshold, maxPct);

        // CTO-rond 2026-05-23 H1 + security-auditor — post-archive circuit-breaker.
        // Pre-COUNT INNAN ExecuteUpdate så vi kan avbryta destruktiv operation
        // utan side-effect (Beck 2002 fail-fast). Skydd mot ofog-konfig från
        // operator-sidan (uppströms-skydd täcker bara JobTech-sidan).
        var activeCount = await missTracker
            .CountActiveJobAdsAsync(source, cancellationToken)
            .ConfigureAwait(false);
        var candidateCount = await missTracker
            .CountArchiveCandidatesAsync(source, threshold, cancellationToken)
            .ConfigureAwait(false);

        // Ratio = 0 om inga aktiva (defense — undvik div-by-zero; ger ratio < maxPct → släpps).
        var ratio = activeCount > 0 ? (double)candidateCount / activeCount : 0.0;

        if (ratio > maxPct)
        {
            // Audit-rad SKRIVS FÖRE throw så det finns granskningsbart spår även
            // efter Hangfire-retry-loop (CTO H1.C). En audit-rad per försök.
            var abortedAt = clock.UtcNow;
            LogAbortedDueToRatio(logger, source.Value, candidateCount, activeCount, ratio, maxPct);

            await auditor.RecordAsync(new JobAdsRetentionCompleted(
                AggregateId: runId,
                OccurredAt: abortedAt,
                Source: source.Value,
                Reason: "snapshot-miss",
                ArchivedCount: 0,
                Threshold: threshold,
                ParsedTotalLastSnapshot: null,
                Max7dObservedSnapshot: null,
                ThresholdAborted: true,
                AbortReason: "max-archive-pct-exceeded",
                StartedAt: startedAt,
                CompletedAt: abortedAt), cancellationToken).ConfigureAwait(false);

            // Throw fail-loud (CTO H1.D) — Hangfire-retry surfar via dashboard +
            // CloudWatch alarm; operatör måste granska konfig innan retention
            // kan fortsätta. DomainException-typ (code+message-konstruktor)
            // bubblar via Hangfire-runner → JobFailedException → metric filter
            // event_name=retention_aborted ovan ger CloudWatch alarm.
            throw new DomainException(
                "RetainPlatsbankenJobAds.MaxArchivePctExceeded",
                $"Retention aborted: archive ratio {ratio:P1} " +
                $"(candidates={candidateCount}/active={activeCount}) " +
                $"exceeds MaxArchivePctPerRun {maxPct:P1}. " +
                $"Config inspection required.");
        }

        var archivedCount = await missTracker
            .ArchiveJobAdsWithMissCountAtLeastAsync(source, threshold, startedAt, cancellationToken)
            .ConfigureAwait(false);

        var completedAt = clock.UtcNow;
        LogCompleted(logger, source.Value, threshold, archivedCount,
            (completedAt - startedAt).TotalSeconds);

        // Aggregerad audit-rad (ADR 0035) — alltid skrivs, även vid 0 arkiverade.
        // GDPR Art. 30 accountability: "behandlingsaktivitet har körts".
        await auditor.RecordAsync(new JobAdsRetentionCompleted(
            AggregateId: runId,
            OccurredAt: completedAt,
            Source: source.Value,
            Reason: "snapshot-miss",
            ArchivedCount: archivedCount,
            Threshold: threshold,
            ParsedTotalLastSnapshot: null,    // okänt i retention-jobbet; lever i snapshot-audit-raden
            Max7dObservedSnapshot: null,
            ThresholdAborted: false,
            AbortReason: null,
            StartedAt: startedAt,
            CompletedAt: completedAt), cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(EventId = 5701, Level = LogLevel.Information,
        Message = "RetainPlatsbankenJobAdsJob: startad — source={Source}, threshold={Threshold} (N konsekutiva snapshot-misses), maxArchivePct={MaxArchivePct}.")]
    private static partial void LogStarted(ILogger logger, string source, int threshold, double maxArchivePct);

    [LoggerMessage(EventId = 5702, Level = LogLevel.Information,
        Message = "RetainPlatsbankenJobAdsJob: klart — source={Source}, threshold={Threshold}, archived={ArchivedCount}, durationSec={DurationSec}.")]
    private static partial void LogCompleted(
        ILogger logger, string source, int threshold, int archivedCount, double durationSec);

    // event_name=-konvention för CloudWatch metric filter (ADR 0036) + security
    // (operator-ofog-detektering surfar via alarm).
    [LoggerMessage(EventId = 5703, Level = LogLevel.Error,
        Message = "event_name=retention_aborted source={Source} candidates={Candidates} active={Active} ratio={Ratio} maxArchivePct={MaxArchivePct} — ABORT pga post-archive circuit-breaker (operator-ofog eller anomalt utfall). Inga rader arkiverade. Hangfire-retry kommer fail-loop tills config korrigerad.")]
    private static partial void LogAbortedDueToRatio(
        ILogger logger, string source, int candidates, int active, double ratio, double maxArchivePct);
}
