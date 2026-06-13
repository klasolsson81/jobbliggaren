using Jobbliggaren.Application.JobAds.Jobs.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Application.JobAds.Jobs.BackfillJobAdKlass2;

/// <summary>
/// Fas B2 (2026-06-08, ADR 0067 Beslut 2 — Platsbanken sök-paritet) — engångs-
/// backfill av Klass 2-kolumnerna <c>employment_type_concept_id</c> +
/// <c>worktime_extent_concept_id</c> för JobAds vars <c>raw_payload</c> saknar
/// <c>employment_type</c>/<c>working_hours_type</c>-keys (alla rader importerade
/// före B2:s <c>JobTechHit</c>-POCO-tillägg). Tunn wrapper kring
/// <see cref="JobAdRefetchBackfillRunner"/> (senior-cto-advisor Variant H):
/// bidrar endast med NULL-kolumn-predikatet + IOptions-tunables.
///
/// <para>
/// <b>Predikat:</b> <c>EmploymentTypeConceptId IS NULL</c>. Eftersom per-ID-
/// refetch re-skriver HELA raw_payload re-evalueras BÅDA Klass 2-kolumnerna
/// (employment_type + worktime_extent) i samma upsert — employment_type är den
/// kanoniska "saknar Klass 2-data"-markören (båda är top-level i samma payload).
/// </para>
///
/// <para>
/// <b>Engångs-operation</b> — INTE registrerad i RecurringJobRegistrar.
/// Enqueue:as fire-and-forget via admin-endpoint <c>POST /backfill-klass2</c>.
/// Idempotent restart-vänlig via NULL-filtret (runnern). Re-ingest-körningen är
/// en Klas-GO-grindad operativ åtgärd (ADR 0067 Beslut 2 — kolumnerna är NULL
/// tills körningen skett).
/// </para>
/// </summary>
public sealed class BackfillJobAdKlass2Job(
    JobAdRefetchBackfillRunner runner,
    IOptions<BackfillJobAdKlass2Options> options)
{
    public Task<BackfillCounts> RunAsync(CancellationToken cancellationToken)
    {
        var opts = options.Value;
        return runner.RunAsync(
            nullColumnPredicate: j => EF.Property<string?>(j, "EmploymentTypeConceptId") == null,
            options: new BackfillRunnerOptions(
                opts.PerItemDelayMs, opts.MaxItemsPerRun, opts.ProgressLogEvery),
            auditJobType: "backfill-klass2",
            cancellationToken);
    }
}
