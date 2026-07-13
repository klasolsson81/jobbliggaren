using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Jobs.Common;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Application.JobAds.Jobs.BackfillJobAdRequirements;

/// <summary>
/// Fas 4 STEG 4b (F4-4b, ADR 0071/0074/0075) — engångs-re-ingest av
/// arbetsgivar-kraven (<c>must_have</c>/<c>nice_to_have</c>) för JobAds vars
/// <c>raw_payload</c> importerades före POCO-expansionen (alla rader tills
/// körningen skett — samma POCO-rotorsaks-mönster som Klass2). Tunn wrapper kring
/// <see cref="JobAdRefetchBackfillRunner"/> (senior-cto-advisor Variant H,
/// paritet <c>BackfillJobAdKlass2Job</c>): bidrar endast med re-ingest-predikatet
/// + IOptions-tunables.
///
/// <para>
/// <b>Predikat (F4-4b 2A-korrigerad):</b> rader vars <c>raw_payload</c> saknar
/// <c>must_have</c>-nyckeln. Predikatet behöver Npgsql <c>jsonb ?</c>-operatorn och
/// får därför INTE byggas inline här (skulle läcka Npgsql till Application,
/// CLAUDE.md §2.1) — det kommer via <see cref="IJobAdRequirementBackfillFilter"/>
/// (Infrastructure-impl). Per-ID-refetch
/// re-skriver HELA raw_payload → must_have landar + ingest-hooken (UpsertExternalJobAd)
/// kör full extraktion → Requirement-termer populeras (OCH keyword/skill — denna
/// körning subsumerar F4-4:s lokala extraction-backfill).
/// </para>
///
/// <para>
/// <b>Engångs-operation</b> — INTE registrerad i RecurringJobRegistrar.
/// Enqueue:as fire-and-forget via admin-endpoint <c>POST /backfill-requirements</c>.
/// Idempotent restart-vänlig: en re-ingestad rad bär <c>must_have</c> → exkluderas
/// av predikatet vid nästa körning. Re-ingest-körningen är en Klas-GO-grindad
/// operativ åtgärd (paritet Klass2 — kraven är opopulerade tills körd).
/// </para>
/// </summary>
public sealed class BackfillJobAdRequirementsJob(
    JobAdRefetchBackfillRunner runner,
    IJobAdRequirementBackfillFilter filter,
    IOptions<BackfillJobAdRequirementsOptions> options)
{
    public Task<BackfillCounts> RunAsync(CancellationToken cancellationToken)
    {
        var opts = options.Value;
        return runner.RunAsync(
            nullColumnPredicate: filter.RowsMissingRequirements,
            options: new BackfillRunnerOptions(
                opts.PerItemDelayMs, opts.MaxItemsPerRun, opts.ProgressLogEvery),
            auditJobType: "backfill-requirements",
            cancellationToken);
    }
}
