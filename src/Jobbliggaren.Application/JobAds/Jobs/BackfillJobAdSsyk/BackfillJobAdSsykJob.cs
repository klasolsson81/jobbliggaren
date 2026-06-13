using Jobbliggaren.Application.JobAds.Jobs.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Application.JobAds.Jobs.BackfillJobAdSsyk;

/// <summary>
/// STEG 6 (2026-05-24) — engångs-backfill av <c>ssyk_concept_id</c> för JobAds
/// vars <c>raw_payload</c> saknar <c>occupation</c>-key (pre-2026-05-20-
/// <c>JobTechHit.Occupation</c>-fix). Tunn wrapper kring
/// <see cref="JobAdRefetchBackfillRunner"/> (2026-06-08, senior-cto-advisor
/// Variant H): all iterations-/refetch-/throttle-/upsert-mekanik bor i runnern;
/// detta jobb bidrar endast med NULL-kolumn-predikatet (<c>SsykConceptId IS
/// NULL</c>) + sina IOptions-tunables. Publikt beteende (signatur, endpoint,
/// Worker-wrapper) oförändrat när loop-kroppen flyttades till runnern.
///
/// <para>
/// <b>Varför per-ID-refetch (i runnern):</b> JobTech <c>/v2/snapshot</c>
/// trunkerar icke-deterministiskt — per-ID-fetch ger deterministisk coverage
/// (ADR 0032-amendment 2026-05-16, architect-rond 2026-05-24).
/// </para>
/// </summary>
public sealed class BackfillJobAdSsykJob(
    JobAdRefetchBackfillRunner runner,
    IOptions<BackfillJobAdSsykOptions> options)
{
    public Task<BackfillCounts> RunAsync(CancellationToken cancellationToken)
    {
        var opts = options.Value;
        return runner.RunAsync(
            nullColumnPredicate: j => EF.Property<string?>(j, "SsykConceptId") == null,
            options: new BackfillRunnerOptions(
                opts.PerItemDelayMs, opts.MaxItemsPerRun, opts.ProgressLogEvery),
            auditJobType: "backfill",
            cancellationToken);
    }
}
