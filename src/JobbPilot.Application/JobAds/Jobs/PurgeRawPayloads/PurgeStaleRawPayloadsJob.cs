using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.JobAds.Abstractions;
using JobbPilot.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobbPilot.Application.JobAds.Jobs.PurgeRawPayloads;

/// <summary>
/// Hangfire RecurringJob (cron <c>30 4 * * *</c> per CTO-rond 2026-05-13 punkt 8 —
/// 30-min-padding efter <c>hard-delete-accounts</c>). Null:ar <c>raw_payload</c>
/// på alla JobAds där <c>published_at</c> är äldre än
/// <see cref="JobSourceRetentionOptions.RawPayloadRetentionDays"/>.
///
/// <para>
/// GDPR Art. 5(1)(c) (data-minimering) + Art. 5(1)(e) (lagrings-begränsning) —
/// rekryterar-PII som överlever sanitizer:n (free-text-yta i description) försvinner
/// efter 30 dagar. ADR 0032 §8-amendment 2026-05-12. Sanering körs som
/// <c>ExecuteUpdateAsync</c>-LINQ utan EF-tracking (CLAUDE.md §3.6 OK —
/// fortfarande IAppDbContext-bunden LINQ-genererad SQL, ingen raw text).
/// </para>
///
/// <para>
/// Audit-wire av <c>RawPayloadPurgedDomainEvent</c> defereras till TD-73
/// right-to-erasure-batch (gemensam audit-wire via <c>ISystemEventAuditor</c>
/// per senior-cto-advisor 2026-05-13 punkt 5). Interim: count + cutoff
/// loggas strukturerat via Serilog → CloudWatch.
/// </para>
/// </summary>
public sealed partial class PurgeStaleRawPayloadsJob(
    IAppDbContext db,
    IDateTimeProvider clock,
    IOptions<JobSourceRetentionOptions> optionsAccessor,
    ILogger<PurgeStaleRawPayloadsJob> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var retentionDays = optionsAccessor.Value.RawPayloadRetentionDays;
        if (retentionDays < 1)
        {
            // Range-guard speglar JobTechOptions [Range(1,365)] men ger tydlig log
            // om felaktig config nått hit (skulle fångats av ValidateOnStart men
            // defense-in-depth är gratis).
            LogInvalidRetention(logger, retentionDays);
            return;
        }

        var cutoff = clock.UtcNow.AddDays(-retentionDays);

        var rowsAffected = await db.JobAds
            .Where(j => j.RawPayload != null && j.PublishedAt < cutoff)
            .ExecuteUpdateAsync(
                s => s.SetProperty(j => j.RawPayload, _ => null),
                cancellationToken);

        LogPurged(logger, rowsAffected, cutoff, retentionDays);
    }

    [LoggerMessage(EventId = 5501, Level = LogLevel.Information,
        Message = "PurgeStaleRawPayloadsJob: null:ade raw_payload på {RowsAffected} rader (cutoff={Cutoff:O}, retentionDays={RetentionDays}).")]
    private static partial void LogPurged(ILogger logger, int rowsAffected, DateTimeOffset cutoff, int retentionDays);

    [LoggerMessage(EventId = 5502, Level = LogLevel.Error,
        Message = "PurgeStaleRawPayloadsJob: ogiltigt RawPayloadRetentionDays={RetentionDays} — hoppar över. Kontrollera JobTech-config.")]
    private static partial void LogInvalidRetention(ILogger logger, int retentionDays);
}
