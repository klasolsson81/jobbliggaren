using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Application.JobAds.Jobs.PurgeRawPayloads;

/// <summary>
/// Hangfire RecurringJob (cron <c>30 4 * * *</c> per CTO-rond 2026-05-13 punkt 8 —
/// 30-min-padding efter <c>hard-delete-accounts</c>). Null:ar <c>raw_payload</c>
/// på alla JobAds där <c>published_at</c> är äldre än
/// <see cref="JobSourceRetentionOptions.RawPayloadRetentionDays"/>.
///
/// <para>
/// GDPR Art. 5(1)(c) (data-minimering) + Art. 5(1)(e) (lagrings-begränsning). Sanering
/// körs som <c>ExecuteUpdateAsync</c>-LINQ utan EF-tracking (CLAUDE.md §3.6 OK —
/// fortfarande IAppDbContext-bunden LINQ-genererad SQL, ingen raw text).
/// ADR 0032 §8-amendment 2026-05-12.
/// </para>
///
/// <para>
/// <b>⚠ TRUTH-SYNC 2026-07-13 (#842).</b> This paragraph used to claim that "recruiter PII
/// which survives the sanitizer (the free-text surface in description) disappears after 30
/// days". <b>That was false in the most direct way possible: this job never touches
/// <c>description</c>.</b> It nulls <c>raw_payload</c> and nothing else (see
/// <c>PurgeRawPayloadsAsync</c> below). It claimed to erase precisely the PII it cannot
/// reach — and ADR 0032 §8 recorded that claim as one of two GDPR mitigations for inbound
/// recruiter PII. The real control is <c>RecruiterContactRedactor</c> at ingest (ADR 0106
/// Tier A); this job's honest scope is raw_payload retention, which is all it ever did.
/// </para>
///
/// <para>
/// <b>⚠ BLAST RADIUS — nulling raw_payload destroys SEVEN columns, not one (#824 / #841).</b>
/// <c>job_ads</c> carries seven STORED generated columns derived from <c>raw_payload</c>
/// (<c>organization_number</c>, <c>municipality_concept_id</c>, <c>ssyk_concept_id</c>,
/// <c>region_concept_id</c>, <c>occupation_group_concept_id</c>, <c>employment_type_concept_id</c>,
/// <c>worktime_extent_concept_id</c> — see <c>JobAdConfiguration</c>). Postgres RECOMPUTES a stored
/// generated column on every UPDATE of its base, so this job silently nulls all seven. Known consumers,
/// all of which therefore degrade for an ad past the horizon:
/// <list type="bullet">
/// <item><c>JobAdSearchComposition</c> / <c>JobAdSearchQuery</c> — facet-filtered search drops the ad;</item>
/// <item><c>PerUserJobAdSearchQuery</c> — per-user background matching drops it;</item>
/// <item><c>CompanyWatchScanJob</c> — the followed-company location filter misses it;</item>
/// <item><c>GetEmployerApplicationHistory</c> / <c>GetEmployerApplicationCountBatch</c> — the
/// application can no longer be attributed to an employer (#824);</item>
/// <item><b><c>CreateApplicationFromJobAdCommandHandler</c> — the worst one: it FREEZES
/// <c>MunicipalityConceptId</c> into <c>AdSnapshot</c> at apply time, so applying to an
/// already-purged ad captures a permanent NULL into the snapshot that was built to outlive the ad;</b></item>
/// <item><c>GetActivityReportQueryHandler</c>.</item>
/// </list>
/// This job's own stated purpose says nothing about any of that — which is exactly why the defect
/// survived four separate column additions (see the ADR 0032 §8-amendment 2026-07-12). Root cause is
/// fixed in #841 (materialise the seven as ordinary, C#-written ingest columns);
/// do NOT "fix" it by exempting ads from the purge (that subordinates a GDPR minimisation control to
/// a search-correctness need — senior-cto-advisor, 2026-07-12).
/// </para>
///
/// <para>
/// <b>⚠ The retention rule this job implements is NOT the documented one (#845).</b> The daily
/// full-backfill sync (<c>SyncPlatsbankenSnapshotJob</c>, cron <c>0 2 * * *</c>) rewrites
/// <c>raw_payload</c> unconditionally for every ad still in the feed, so for a still-listed ad this
/// purge is undone ~21.5h later, every day. The de-facto rule is "30 days after the ad LEAVES the
/// feed", not "30 days after publication". <b>And the mitigation is largely illusory (#842):</b> the
/// recruiter free-text this job exists to scrub also lives in the ordinary <c>job_ads.description</c>
/// column, which is never purged — so the identical text survives, and remains FTS-searchable via
/// <c>search_vector</c>.
/// </para>
///
/// <para>
/// Audit-wire av <c>RawPayloadPurgedDomainEvent</c> defereras till TD-73
/// right-to-erasure-batch (gemensam audit-wire via <c>ISystemEventAuditor</c>
/// per senior-cto-advisor 2026-05-13 punkt 5). Interim: count + cutoff
/// loggas strukturerat via ILogger (MEL → Seq-sink, TD-104).
/// </para>
/// </summary>
public sealed partial class PurgeStaleRawPayloadsJob(
    IAppDbContext db,
    IDateTimeProvider clock,
    IOptions<JobSourceRetentionOptions> optionsAccessor,
    ISystemEventAuditor auditor,
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

        // Per-run-Guid för audit-rad (ADR 0035 §2).
        var runId = Guid.NewGuid();
        var startedAt = clock.UtcNow;
        var cutoff = startedAt.AddDays(-retentionDays);

        var rowsAffected = await db.JobAds
            .Where(j => j.RawPayload != null && j.PublishedAt < cutoff)
            .ExecuteUpdateAsync(
                s => s.SetProperty(j => j.RawPayload, _ => null),
                cancellationToken);

        LogPurged(logger, rowsAffected, cutoff, retentionDays);

        // Audit-wire α (ADR 0035 + ADR 0032 §8 amendment 2026-05-13).
        // Skriv alltid audit-rad — även vid rowsAffected=0 är det relevant
        // accountability-information (GDPR Art. 30 "behandlingsaktivitet har körts").
        await auditor.RecordAsync(new RawPayloadPurged(
            AggregateId: runId,
            OccurredAt: clock.UtcNow,
            RowsAffected: rowsAffected,
            Cutoff: cutoff,
            RetentionDays: retentionDays), cancellationToken);
    }

    [LoggerMessage(EventId = 5501, Level = LogLevel.Information,
        Message = "PurgeStaleRawPayloadsJob: null:ade raw_payload på {RowsAffected} rader (cutoff={Cutoff:O}, retentionDays={RetentionDays}).")]
    private static partial void LogPurged(ILogger logger, int rowsAffected, DateTimeOffset cutoff, int retentionDays);

    [LoggerMessage(EventId = 5502, Level = LogLevel.Error,
        Message = "PurgeStaleRawPayloadsJob: ogiltigt RawPayloadRetentionDays={RetentionDays} — hoppar över. Kontrollera JobTech-config.")]
    private static partial void LogInvalidRetention(ILogger logger, int retentionDays);
}
