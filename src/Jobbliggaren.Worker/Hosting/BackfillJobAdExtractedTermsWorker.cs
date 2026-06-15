using Hangfire;
using Jobbliggaren.Application.JobAds.Jobs.BackfillJobAdExtractedTerms;

namespace Jobbliggaren.Worker.Hosting;

/// <summary>
/// Fas 4 STEG 4 (F4-4, ADR 0071/0074 Path C) — Worker-wrapper för
/// <see cref="BackfillJobAdExtractedTermsJob"/> som applicerar Hangfire
/// <see cref="DisableConcurrentExecutionAttribute"/> utan att läcka Hangfire-
/// beroende till Application-lagret (Clean Arch — ADR 0023, samma mönster som
/// <see cref="BackfillJobAdKlass2Worker"/>).
///
/// <para>
/// Timeout 7200 s (2 h): LOKAL re-projektion (ingen JobTech-fetch, ingen
/// per-item-throttle), så betydligt snabbare än ssyk/Klass2-backfillarna — men
/// 54k rader × (Snowball + skill-index-lookup + SaveChanges) tar ändå tid.
/// Idempotent — avbruten körning plockas upp av nästa enqueue (extracted_lexemes
/// IS NULL-filter).
/// </para>
/// </summary>
public sealed class BackfillJobAdExtractedTermsWorker(BackfillJobAdExtractedTermsJob job)
{
    [DisableConcurrentExecution(timeoutInSeconds: 7200)]
    public Task RunAsync(CancellationToken cancellationToken) =>
        job.RunAsync(cancellationToken);
}
